using System.Collections.Concurrent;
using System.Diagnostics;
#if WINDOWS
using System.Formats.Asn1;
#endif
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
#if WINDOWS
using System.Security.Cryptography.X509Certificates;
#endif
using Coder.Desktop.Vpn.Utilities;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.Security.Extensions;
#endif

namespace Coder.Desktop.Vpn.Service;

public interface IDownloader
{
    Task<DownloadTask> StartDownloadAsync(HttpRequestMessage req, string destinationPath, IDownloadValidator validator,
        CancellationToken ct = default);
}

public interface IDownloadValidator
{
    /// <summary>
    ///     Validates the downloaded file at the given path. This method should throw an exception if the file is invalid.
    /// </summary>
    /// <param name="path">The path of the file</param>
    /// <param name="ct">Cancellation token</param>
    Task ValidateAsync(string path, CancellationToken ct = default);
}

public class NullDownloadValidator : IDownloadValidator
{
    public static NullDownloadValidator Instance => new();

    public Task ValidateAsync(string path, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

#if WINDOWS
/// <summary>
///     Ensures the downloaded binary is signed by the expected authenticode organization.
/// </summary>
public class AuthenticodeDownloadValidator : IDownloadValidator
{
    public static readonly AuthenticodeDownloadValidator Coder = new("Coder Technologies Inc.");

    private static readonly Oid CertificatePoliciesOid = new("2.5.29.32", "Certificate Policies");

    private static readonly Oid ExtendedValidationCodeSigningOid =
        new("2.23.140.1.3", "Extended Validation (EV) code signing");

    private readonly string _expectedName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public AuthenticodeDownloadValidator(string expectedName)
    {
        _expectedName = expectedName;
    }

    public async Task ValidateAsync(string path, CancellationToken ct = default)
    {
        FileSignatureInfo fileSigInfo;
        await using (var fileStream = File.OpenRead(path))
        {
            fileSigInfo = FileSignatureInfo.GetFromFileStream(fileStream);
        }

        if (fileSigInfo.State != SignatureState.SignedAndTrusted)
            throw new Exception(
                $"File is not signed and trusted with an Authenticode signature: State={fileSigInfo.State}, StateReason={fileSigInfo.StateReason}");

        // Coder will only use embedded signatures because we are downloading
        // individual binaries and not installers which can ship catalog files.
        if (fileSigInfo.Kind != SignatureKind.Embedded)
            throw new Exception($"File is not signed with an embedded Authenticode signature: Kind={fileSigInfo.Kind}");

        // We want to wrap any exception from IsExtendedValidationCertificate
        // with a nicer error message, but we don't want to wrap the "false"
        // result exception.
        bool isExtendedValidation;
        try
        {
            isExtendedValidation = IsExtendedValidationCertificate(fileSigInfo.SigningCertificate);
        }
        catch (Exception e)
        {
            throw new Exception(
                "Could not check if file is signed with an Extended Validation Code Signing certificate", e);
        }

        if (!isExtendedValidation)
            throw new Exception(
                $"File is not signed with an Extended Validation Code Signing certificate (missing policy {ExtendedValidationCodeSigningOid.Value} - {ExtendedValidationCodeSigningOid.FriendlyName})");

        var actualName = fileSigInfo.SigningCertificate.GetNameInfo(X509NameType.SimpleName, false);
        if (actualName != _expectedName)
            throw new Exception(
                $"File is signed by an unexpected certificate: ExpectedName='{_expectedName}', ActualName='{actualName}'");
    }

    /// <summary>
    ///     Checks if the given certificate is an Extended Validation Code Signing certificate.
    /// </summary>
    /// <param name="cert">The cert to test</param>
    /// <returns>Whether the certificate is an Extended Validation Code Signing certificate</returns>
    /// <exception cref="Exception">If the certificate extensions could not be parsed</exception>
    private static bool IsExtendedValidationCertificate(X509Certificate2 cert)
    {
        ArgumentNullException.ThrowIfNull(cert);

        // RFC 5280 4.2: "A certificate MUST NOT include more than one instance
        // of a particular extension."
        var policyExtensions = cert.Extensions.Where(e => e.Oid?.Value == CertificatePoliciesOid.Value).ToList();
        if (policyExtensions.Count == 0)
            return false;
        Assert(policyExtensions.Count == 1, "certificate contains more than one CertificatePolicies extension");
        var certificatePoliciesExt = policyExtensions[0];

        // RFC 5280 4.2.1.4
        // certificatePolicies ::= SEQUENCE SIZE (1..MAX) OF PolicyInformation
        //
        // PolicyInformation ::= SEQUENCE {
        //   policyIdentifier   CertPolicyId,
        //   policyQualifiers   SEQUENCE SIZE (1..MAX) OF PolicyQualifierInfo OPTIONAL
        // }
        try
        {
            AsnDecoder.ReadSequence(certificatePoliciesExt.RawData, AsnEncodingRules.DER, out var originalContentOffset,
                out var contentLength, out var bytesConsumed);
            Assert(bytesConsumed == certificatePoliciesExt.RawData.Length, "incorrect outer sequence length");
            Assert(originalContentOffset >= 0, "invalid outer sequence content offset");
            Assert(contentLength > 0, "invalid outer sequence content length");

            var contentOffset = originalContentOffset;
            var endOffset = originalContentOffset + contentLength;
            Assert(endOffset <= certificatePoliciesExt.RawData.Length, "invalid outer sequence end offset");

            // For each policy...
            while (contentOffset < endOffset)
            {
                // Parse a sequence from [contentOffset:].
                var slice = certificatePoliciesExt.RawData.AsSpan(contentOffset, endOffset - contentOffset);
                AsnDecoder.ReadSequence(slice, AsnEncodingRules.DER, out var innerContentOffset,
                    out var innerContentLength, out var innerBytesConsumed);
                Assert(innerBytesConsumed <= slice.Length, "incorrect inner sequence length");
                Assert(innerContentOffset >= 0, "invalid inner sequence content offset");
                Assert(innerContentLength > 0, "invalid inner sequence content length");
                Assert(innerContentOffset + innerContentLength <= slice.Length, "invalid inner sequence end offset");

                // Advance the outer offset by the consumed bytes.
                contentOffset += innerBytesConsumed;

                // Parse the first value in the sequence as an Oid.
                slice = slice.Slice(innerContentOffset, innerContentLength);
                var oid = AsnDecoder.ReadObjectIdentifier(slice, AsnEncodingRules.DER, out var oidBytesConsumed);
                Assert(oidBytesConsumed > 0, "invalid inner sequence OID length");
                Assert(oidBytesConsumed <= slice.Length, "invalid inner sequence OID length");
                if (oid == ExtendedValidationCodeSigningOid.Value)
                    return true;

                // We don't need to parse the rest of the data in the sequence,
                // we can just move on to the next iteration.
            }
        }
        catch (Exception e)
        {
            throw new Exception(
                $"Could not parse {CertificatePoliciesOid.Value} ({CertificatePoliciesOid.FriendlyName}) extension in certificate",
                e);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception("Failed certificate parse assertion: " + message);
    }
}
#endif

public class AssemblyVersionDownloadValidator : IDownloadValidator
{
    private readonly int _expectedMajor;
    private readonly int _expectedMinor;
    private readonly int _expectedBuild;
    private readonly int _expectedPrivate;

    private readonly Version _expectedVersion;

    // ReSharper disable once ConvertToPrimaryConstructor
    public AssemblyVersionDownloadValidator(int expectedMajor, int expectedMinor, int expectedBuild = -1,
        int expectedPrivate = -1)
    {
        _expectedMajor = expectedMajor;
        _expectedMinor = expectedMinor;
        _expectedBuild = expectedBuild < 0 ? -1 : expectedBuild;
        _expectedPrivate = expectedPrivate < 0 ? -1 : expectedPrivate;
        if (_expectedBuild == -1 && _expectedPrivate != -1)
            throw new ArgumentException("Build must be set if Private is set", nameof(expectedPrivate));

        // Unfortunately the Version constructor throws an exception if the
        // build or revision is -1. You need to use the specific constructor
        // with the correct number of parameters.
        //
        // This is only for error rendering purposes anyways.
        if (_expectedBuild == -1)
            _expectedVersion = new Version(_expectedMajor, _expectedMinor);
        else if (_expectedPrivate == -1)
            _expectedVersion = new Version(_expectedMajor, _expectedMinor, _expectedBuild);
        else
            _expectedVersion = new Version(_expectedMajor, _expectedMinor, _expectedBuild, _expectedPrivate);
    }

    public Task ValidateAsync(string path, CancellationToken ct = default)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        if (string.IsNullOrEmpty(info.ProductVersion))
            throw new Exception("File ProductVersion is empty or null, was the binary compiled correctly?");
        if (!Version.TryParse(info.ProductVersion, out var productVersion))
            throw new Exception($"File ProductVersion '{info.ProductVersion}' is not a valid version string");

        // If the build or private are -1 on the expected version, they are ignored.
        if (productVersion.Major != _expectedMajor || productVersion.Minor != _expectedMinor ||
            (_expectedBuild != -1 && productVersion.Build != _expectedBuild) ||
            (_expectedPrivate != -1 && productVersion.Revision != _expectedPrivate))
            throw new Exception(
                $"File ProductVersion does not match expected version: Actual='{info.ProductVersion}', Expected='{_expectedVersion}'");

        return Task.CompletedTask;
    }
}

/// <summary>
///     Combines multiple download validators into a single validator. All validators will be run in order.
/// </summary>
public class CombinationDownloadValidator : IDownloadValidator
{
    private readonly List<IDownloadValidator> _validators;

    /// <param name="validators">Validators to run</param>
    public CombinationDownloadValidator(params IDownloadValidator[] validators)
    {
        _validators = validators.ToList();
    }

    public async Task ValidateAsync(string path, CancellationToken ct = default)
    {
        foreach (var validator in _validators)
            await validator.ValidateAsync(path, ct);
    }

    public void Add(IDownloadValidator validator)
    {
        _validators.Add(validator);
    }
}

/// <summary>
///     Handles downloading files from the internet. Downloads are performed asynchronously using DownloadTask.
///     Single-flight is provided to avoid performing the same download multiple times.
/// </summary>
public class Downloader : IDownloader
{
    private readonly ConcurrentDictionary<string /* DestinationPath */, DownloadTask> _downloads = new();
    private readonly ILogger<Downloader> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public Downloader(ILogger<Downloader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Starts a download with the given request. The If-None-Match header will be set to the SHA1 ETag of any existing
    ///     file in the destination location.
    /// </summary>
    /// <param name="req">Request message</param>
    /// <param name="destinationPath">Path to write file to (will be overwritten)</param>
    /// <param name="validator">Validator for the downloaded file</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <c>DownloadTask</c> representing the ongoing download operation after it starts</returns>
    public async Task<DownloadTask> StartDownloadAsync(HttpRequestMessage req, string destinationPath,
        IDownloadValidator validator, CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var task = _downloads.GetOrAdd(destinationPath,
                _ => new DownloadTask(_logger, req, destinationPath, validator));
            // EnsureStarted is a no-op if we didn't create a new DownloadTask.
            // So, we will only remove the destination once for each time we start a new task.
            task.EnsureStarted(tsk =>
            {
                // remove the key first, before checking the exception, to ensure
                // we still clean up.
                _downloads.TryRemove(destinationPath, out _);
                if (tsk.Exception == null) return;

                if (tsk.Exception.InnerException != null)
                    ExceptionDispatchInfo.Capture(tsk.Exception.InnerException).Throw();

                // not sure if this is hittable, but just in case:
                throw tsk.Exception;
            }, ct);

            // If the existing (or new) task is for the same URL, return it.
            if (task.Request.RequestUri == req.RequestUri)
                return task;

            // If the existing task is for a different URL, await its completion
            // then retry the loop to create a new task. This could potentially
            // get stuck if there are a lot of download operations for different
            // URLs and the same destination path, but in our use case this
            // shouldn't happen unless the user keeps changing the access URL.
            _logger.LogWarning(
                "Download for '{DestinationPath}' is already in progress, but is for a different Url - awaiting completion",
                destinationPath);
            await TaskOrCancellation(task.Task, ct);
        }
    }

    /// <summary>
    ///     TaskOrCancellation waits for either the task to complete, or the given token to be canceled.
    /// </summary>
    internal static async Task TaskOrCancellation(Task task, CancellationToken cancellationToken)
    {
        var cancellationTask = new TaskCompletionSource();
        await using (cancellationToken.Register(() => cancellationTask.TrySetCanceled()))
        {
            // Wait for either the task or the cancellation
            var completedTask = await Task.WhenAny(task, cancellationTask.Task);
            // Await to propagate exceptions, if any
            await completedTask;
        }
    }
}

/// <summary>
///     Downloads a Url to a file on disk. The download will be written to a temporary file first, then moved to the final
///     destination. The SHA1 of any existing file will be calculated and used as an ETag to avoid downloading the file if
///     it hasn't changed.
/// </summary>
public class DownloadTask
{
    private const int BufferSize = 64 * 1024;
    private const string XOriginalContentLengthHeader = "X-Original-Content-Length"; // overrides Content-Length if available

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
    });
    private readonly string _destinationDirectory;

    private readonly ILogger _logger;

    private readonly RaiiSemaphoreSlim _semaphore = new(1, 1);
    private readonly IDownloadValidator _validator;
    private readonly string _destinationPath;
    private readonly string _tempDestinationPath;

    public readonly HttpRequestMessage Request;

    public Task Task { get; private set; } = null!; // Set in EnsureStartedAsync
    public bool DownloadStarted { get; private set; } // Whether we've received headers yet and started the actual download
    public ulong BytesWritten { get; private set; }
    public ulong? BytesTotal { get; private set; }
    public double? Progress => BytesTotal == null ? null : (double)BytesWritten / BytesTotal.Value;
    public bool IsCompleted => Task.IsCompleted;

    internal DownloadTask(ILogger logger, HttpRequestMessage req, string destinationPath, IDownloadValidator validator)
    {
        _logger = logger;
        Request = req;
        _validator = validator;

        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty", nameof(destinationPath));
        _destinationPath = Path.GetFullPath(destinationPath);
        if (Path.EndsInDirectorySeparator(_destinationPath))
            throw new ArgumentException($"Destination path '{_destinationPath}' must not end in a directory separator",
                nameof(destinationPath));

        _destinationDirectory = Path.GetDirectoryName(_destinationPath)
                                ?? throw new ArgumentException(
                                    $"Destination path '{_destinationPath}' must have a parent directory",
                                    nameof(destinationPath));

        _tempDestinationPath = Path.Combine(_destinationDirectory, "." + Path.GetFileName(_destinationPath) +
                                                                  ".download-" + Path.GetRandomFileName());
    }

    internal void EnsureStarted(Action<Task> continuation, CancellationToken ct = default)
    {
        using var _ = _semaphore.Lock();
        if (Task == null!)
            Task = Start(ct).ContinueWith(continuation, ct);
    }

    /// <summary>
    ///     Starts downloading the file. The request will be performed in this task, but once started, the task will complete
    ///     and the download will continue in the background. The provided CancellationToken can be used to cancel the
    ///     download.
    /// </summary>
    private async Task Start(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_destinationDirectory);

        // If the destination path exists, generate a Coder SHA1 ETag and send
        // it in the If-None-Match header to the server.
        if (File.Exists(_destinationPath))
        {
            await using var stream = File.OpenRead(_destinationPath);
            var etag = Convert.ToHexString(await SHA1.HashDataAsync(stream, ct)).ToLower();
            Request.Headers.Add("If-None-Match", "\"" + etag + "\"");
        }

        var res = await HttpClient.SendAsync(Request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (res.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogInformation("File has not been modified, skipping download");
            try
            {
                await _validator.ValidateAsync(_destinationPath, ct);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Existing file '{DestinationPath}' failed custom validation", _destinationPath);
                throw new Exception("Existing file failed validation after 304 Not Modified", e);
            }

            return;
        }

        if (res.StatusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning("Failed to download file '{Request.RequestUri}': {StatusCode} {ReasonPhrase}",
                Request.RequestUri, res.StatusCode,
                res.ReasonPhrase);
            throw new HttpRequestException(
                $"Failed to download file '{Request.RequestUri}': {(int)res.StatusCode} {res.ReasonPhrase}");
        }

        if (res.Content == null)
        {
            _logger.LogWarning("File {Request.RequestUri} has no content", Request.RequestUri);
            throw new HttpRequestException("Response has no content");
        }

        if (res.Content.Headers.ContentLength >= 0)
            BytesTotal = (ulong)res.Content.Headers.ContentLength;

        // X-Original-Content-Length overrules Content-Length if set.
        if (res.Headers.TryGetValues(XOriginalContentLengthHeader, out var headerValues))
        {
            // If there are multiple we only look at the first one.
            var headerValue = headerValues.ToList().FirstOrDefault();
            if (!string.IsNullOrEmpty(headerValue) && ulong.TryParse(headerValue, out var originalContentLength))
                BytesTotal = originalContentLength;
            else
                _logger.LogWarning(
                    "Failed to parse {XOriginalContentLengthHeader} header value '{HeaderValue}'",
                    XOriginalContentLengthHeader, headerValue);
        }

        await Download(res, ct);
    }

    private async Task Download(HttpResponseMessage res, CancellationToken ct)
    {
        DownloadStarted = true;
        try
        {
            var sha1 = res.Headers.Contains("ETag") ? SHA1.Create() : null;
            FileStream tempFile;
            try
            {
                tempFile = File.Create(_tempDestinationPath, BufferSize, FileOptions.SequentialScan);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to create temporary file '{TempDestinationPath}'", _tempDestinationPath);
                throw;
            }

            await using (tempFile)
            {
                var stream = await res.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[BufferSize];
                int n;
                while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await tempFile.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha1?.TransformBlock(buffer, 0, n, null, 0);
                    BytesWritten += (ulong)n;
                }
            }

            BytesTotal ??= BytesWritten;
            if (BytesWritten != BytesTotal)
                throw new IOException(
                    $"Downloaded file size does not match expected response content length: Expected={BytesTotal}, BytesWritten={BytesWritten}");

            // Verify the ETag if it was sent by the server.
            if (res.Headers.Contains("ETag") && sha1 != null)
            {
                var etag = res.Headers.ETag!.Tag.Trim('"');
                _ = sha1.TransformFinalBlock([], 0, 0);
                var hashStr = Convert.ToHexString(sha1.Hash!).ToLower();
                if (etag != hashStr)
                    throw new HttpRequestException(
                        $"ETag does not match SHA1 hash of downloaded file: ETag='{etag}', Local='{hashStr}'");
            }

            try
            {
                await _validator.ValidateAsync(_tempDestinationPath, ct);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Downloaded file '{TempDestinationPath}' failed custom validation",
                    _tempDestinationPath);
                throw new HttpRequestException("Downloaded file failed validation", e);
            }

            File.Move(_tempDestinationPath, _destinationPath, true);
        }
        catch
        {
#if DEBUG
            _logger.LogWarning("Not deleting temporary file '{TempDestinationPath}' in debug mode",
                _tempDestinationPath);
#else
            try
            {
                if (File.Exists(_tempDestinationPath))
                    File.Delete(_tempDestinationPath);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to delete temporary file '{TempDestinationPath}'", _tempDestinationPath);
            }
#endif
            throw;
        }
    }
}

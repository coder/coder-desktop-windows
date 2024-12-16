using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Coder.Desktop.Vpn.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Security.Extensions;

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

/// <summary>
///     Ensures the downloaded binary is signed by the expected authenticode organization.
/// </summary>
public class AuthenticodeDownloadValidator : IDownloadValidator
{
    private readonly string _expectedName;

    public static AuthenticodeDownloadValidator Coder => new("Coder Technologies Inc.");

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
                $"File is not signed and trusted with an Authenticode signature: State={fileSigInfo.State}");

        // Coder will only use embedded signatures because we are downloading
        // individual binaries and not installers which can ship catalog files.
        if (fileSigInfo.Kind != SignatureKind.Embedded)
            throw new Exception($"File is not signed with an embedded Authenticode signature: Kind={fileSigInfo.Kind}");

        // TODO: check that it's an extended validation certificate

        var actualName = fileSigInfo.SigningCertificate.GetNameInfo(X509NameType.SimpleName, false);
        if (actualName != _expectedName)
            throw new Exception(
                $"File is signed by an unexpected certificate: ExpectedName='{_expectedName}', ActualName='{actualName}'");
    }
}

public class AssemblyVersionDownloadValidator : IDownloadValidator
{
    private readonly string _expectedAssemblyVersion;

    public AssemblyVersionDownloadValidator(string expectedAssemblyVersion)
    {
        _expectedAssemblyVersion = expectedAssemblyVersion;
    }

    public Task ValidateAsync(string path, CancellationToken ct = default)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        if (string.IsNullOrEmpty(info.ProductVersion))
            throw new Exception("File ProductVersion is empty or null, was the binary compiled correctly?");
        if (info.ProductVersion != _expectedAssemblyVersion)
            throw new Exception(
                $"File ProductVersion is '{info.ProductVersion}', but expected '{_expectedAssemblyVersion}'");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Combines multiple download validators into a single validator. All validators will be run in order.
/// </summary>
public class CombinationDownloadValidator : IDownloadValidator
{
    private readonly IDownloadValidator[] _validators;

    /// <param name="validators">Validators to run</param>
    public CombinationDownloadValidator(params IDownloadValidator[] validators)
    {
        _validators = validators;
    }

    public async Task ValidateAsync(string path, CancellationToken ct = default)
    {
        foreach (var validator in _validators)
            await validator.ValidateAsync(path, ct);
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
            var task = _downloads.GetOrAdd(destinationPath,
                _ => new DownloadTask(_logger, req, destinationPath, validator));
            await task.EnsureStartedAsync(ct);

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
            await task.Task;
        }
    }
}

/// <summary>
///     Downloads an Url to a file on disk. The download will be written to a temporary file first, then moved to the final
///     destination. The SHA1 of any existing file will be calculated and used as an ETag to avoid downloading the file if
///     it hasn't changed.
/// </summary>
public class DownloadTask
{
    private const int BufferSize = 4096;

    private static readonly HttpClient HttpClient = new();
    private readonly string _destinationDirectory;

    private readonly ILogger _logger;

    private readonly RaiiSemaphoreSlim _semaphore = new(1, 1);
    private readonly IDownloadValidator _validator;
    public readonly string DestinationPath;

    public readonly HttpRequestMessage Request;
    public readonly string TempDestinationPath;

    public ulong? TotalBytes { get; private set; }
    public ulong BytesRead { get; private set; }
    public Task Task { get; private set; } = null!; // Set in EnsureStartedAsync

    public double? Progress => TotalBytes == null ? null : (double)BytesRead / TotalBytes.Value;
    public bool IsCompleted => Task.IsCompleted;

    internal DownloadTask(ILogger logger, HttpRequestMessage req, string destinationPath, IDownloadValidator validator)
    {
        _logger = logger;
        Request = req;
        _validator = validator;

        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty", nameof(destinationPath));
        DestinationPath = Path.GetFullPath(destinationPath);
        if (Path.EndsInDirectorySeparator(DestinationPath))
            throw new ArgumentException($"Destination path '{DestinationPath}' must not end in a directory separator",
                nameof(destinationPath));

        _destinationDirectory = Path.GetDirectoryName(DestinationPath)
                                ?? throw new ArgumentException(
                                    $"Destination path '{DestinationPath}' must have a parent directory",
                                    nameof(destinationPath));

        TempDestinationPath = Path.Combine(_destinationDirectory, "." + Path.GetFileName(DestinationPath) +
                                                                  ".download-" + Path.GetRandomFileName());
    }

    internal async Task<Task> EnsureStartedAsync(CancellationToken ct = default)
    {
        using var _ = await _semaphore.LockAsync(ct);
        if (Task == null!)
            Task = await StartDownloadAsync(ct);

        return Task;
    }

    /// <summary>
    ///     Starts downloading the file. The request will be performed in this task, but once started, the task will complete
    ///     and the download will continue in the background. The provided CancellationToken can be used to cancel the
    ///     download.
    /// </summary>
    private async Task<Task> StartDownloadAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_destinationDirectory);

        // If the destination path exists, generate a Coder SHA1 ETag and send
        // it in the If-None-Match header to the server.
        if (File.Exists(DestinationPath))
        {
            await using var stream = File.OpenRead(DestinationPath);
            var etag = Convert.ToHexString(await SHA1.HashDataAsync(stream, ct)).ToLower();
            Request.Headers.Add("If-None-Match", "\"" + etag + "\"");
        }

        var res = await HttpClient.SendAsync(Request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (res.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogInformation("File has not been modified, skipping download");
            try
            {
                await _validator.ValidateAsync(DestinationPath, ct);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Existing file '{DestinationPath}' failed custom validation", DestinationPath);
                throw new Exception("Existing file failed validation after 304 Not Modified", e);
            }

            Task = Task.CompletedTask;
            return Task;
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
            TotalBytes = (ulong)res.Content.Headers.ContentLength;

        FileStream tempFile;
        try
        {
            tempFile = File.Create(TempDestinationPath, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create temporary file '{TempDestinationPath}'", TempDestinationPath);
            throw;
        }

        Task = DownloadAsync(res, tempFile, ct);
        return Task;
    }

    private async Task DownloadAsync(HttpResponseMessage res, FileStream tempFile, CancellationToken ct)
    {
        try
        {
            var sha1 = res.Headers.Contains("ETag") ? SHA1.Create() : null;
            await using (tempFile)
            {
                var stream = await res.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[BufferSize];
                int n;
                while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await tempFile.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha1?.TransformBlock(buffer, 0, n, null, 0);
                    BytesRead += (ulong)n;
                }
            }

            if (TotalBytes != null && BytesRead != TotalBytes)
                throw new IOException(
                    $"Downloaded file size does not match response Content-Length: Content-Length={TotalBytes}, BytesRead={BytesRead}");

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
                await _validator.ValidateAsync(TempDestinationPath, ct);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Downloaded file '{TempDestinationPath}' failed custom validation",
                    TempDestinationPath);
                throw new HttpRequestException("Downloaded file failed validation", e);
            }

            File.Move(TempDestinationPath, DestinationPath, true);
        }
        finally
        {
#if DEBUG
            _logger.LogWarning("Not deleting temporary file '{TempDestinationPath}' in debug mode",
                TempDestinationPath);
#else
            if (File.Exists(TempDestinationPath))
                File.Delete(TempDestinationPath);
#endif
        }
    }
}

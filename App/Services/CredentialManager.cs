using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.Vpn.Utilities;

namespace Coder.Desktop.App.Services;

public class RawCredentials
{
    public required string CoderUrl { get; set; }
    public required string ApiToken { get; set; }
}

[JsonSerializable(typeof(RawCredentials))]
public partial class RawCredentialsJsonContext : JsonSerializerContext;

public interface ICredentialManager
{
    public event EventHandler<CredentialModel> CredentialsChanged;

    /// <summary>
    ///     Returns cached credentials or an invalid credential model if none are cached. It's preferable to use
    ///     LoadCredentials if you are operating in an async context.
    /// </summary>
    public CredentialModel GetCachedCredentials();

    /// <summary>
    ///     Get any sign-in URL. The returned value is not parsed to check if it's a valid URI.
    /// </summary>
    public Task<string?> GetSignInUri();

    /// <summary>
    ///     Returns cached credentials or loads/verifies them from storage if not cached.
    /// </summary>
    public Task<CredentialModel> LoadCredentials(CancellationToken ct = default);

    public Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct = default);

    public Task ClearCredentials(CancellationToken ct = default);
}

public interface ICredentialBackend
{
    public Task<RawCredentials?> ReadCredentials(CancellationToken ct = default);
    public Task WriteCredentials(RawCredentials credentials, CancellationToken ct = default);
    public Task DeleteCredentials(CancellationToken ct = default);
}

/// <summary>
///     Implements ICredentialManager using the Windows Credential Manager to
///     store credentials.
/// </summary>
public class CredentialManager : ICredentialManager
{
    private const string CredentialsTargetName = "Coder.Desktop.App.Credentials";

    // _opLock is held for the full duration of SetCredentials, and partially
    // during LoadCredentials. _lock protects _inFlightLoad, _loadCts, and
    // writes to _latestCredentials.
    private readonly RaiiSemaphoreSlim _opLock = new(1, 1);

    // _inFlightLoad and _loadCts are set at the beginning of a LoadCredentials
    // call.
    private Task<CredentialModel>? _inFlightLoad;
    private CancellationTokenSource? _loadCts;

    // Reading and writing a reference in C# is always atomic, so this doesn't
    // need to be protected on reads with a lock in GetCachedCredentials.
    //
    // The volatile keyword disables optimizations on reads/writes which helps
    // other threads see the new value quickly (no guarantee that it's
    // immediate).
    private volatile CredentialModel? _latestCredentials;

    private ICredentialBackend Backend { get; } = new WindowsCredentialBackend(CredentialsTargetName);

    private ICoderApiClientFactory CoderApiClientFactory { get; } = new CoderApiClientFactory();

    public CredentialManager()
    {
    }

    public CredentialManager(ICredentialBackend backend, ICoderApiClientFactory coderApiClientFactory)
    {
        Backend = backend;
        CoderApiClientFactory = coderApiClientFactory;
    }

    public event EventHandler<CredentialModel>? CredentialsChanged;

    public CredentialModel GetCachedCredentials()
    {
        // No lock required to read the reference.
        var latestCreds = _latestCredentials;
        // No clone needed as the model is immutable.
        if (latestCreds != null) return latestCreds;

        return new CredentialModel
        {
            State = CredentialState.Unknown,
        };
    }

    public async Task<string?> GetSignInUri()
    {
        try
        {
            var raw = await Backend.ReadCredentials();
            if (raw is not null && !string.IsNullOrWhiteSpace(raw.CoderUrl)) return raw.CoderUrl;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    // LoadCredentials may be preempted by SetCredentials.
    public Task<CredentialModel> LoadCredentials(CancellationToken ct = default)
    {
        // This function is not `async` because we may return an existing task.
        // However, we still want to acquire the lock with the
        // CancellationToken so it can be canceled if needed.
        using var _ = _opLock.LockAsync(ct).Result;

        // If we already have a cached value, return it.
        var latestCreds = _latestCredentials;
        if (latestCreds != null) return Task.FromResult(latestCreds);

        // If we are already loading, return the existing task.
        if (_inFlightLoad != null) return _inFlightLoad;

        // Otherwise, kick off a new load.
        // Note: subsequent loads returned from above will ignore the passed in
        // CancellationToken. We set a maximum timeout of 15 seconds anyway.
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loadCts.CancelAfter(TimeSpan.FromSeconds(15));
        _inFlightLoad = LoadCredentialsInner(_loadCts.Token);
        return _inFlightLoad;
    }

    public async Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct)
    {
        using var _ = await _opLock.LockAsync(ct);

        // If there's an ongoing load, cancel it.
        if (_loadCts != null)
        {
            await _loadCts.CancelAsync();
            _loadCts.Dispose();
            _loadCts = null;
            _inFlightLoad = null;
        }

        if (string.IsNullOrWhiteSpace(coderUrl)) throw new ArgumentException("Coder URL is required", nameof(coderUrl));
        coderUrl = coderUrl.Trim();
        if (coderUrl.Length > 128) throw new ArgumentException("Coder URL is too long", nameof(coderUrl));
        if (!Uri.TryCreate(coderUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Coder URL '{coderUrl}' is not a valid URL", nameof(coderUrl));
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException("Coder URL must be HTTP or HTTPS", nameof(coderUrl));
        if (uri.PathAndQuery != "/") throw new ArgumentException("Coder URL must be the root URL", nameof(coderUrl));
        if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("API token is required", nameof(apiToken));
        apiToken = apiToken.Trim();

        var raw = new RawCredentials
        {
            CoderUrl = coderUrl,
            ApiToken = apiToken,
        };
        var populateCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        populateCts.CancelAfter(TimeSpan.FromSeconds(15));
        var model = await PopulateModel(raw, populateCts.Token);
        await Backend.WriteCredentials(raw, ct);
        UpdateState(model);
    }

    public async Task ClearCredentials(CancellationToken ct = default)
    {
        using var _ = await _opLock.LockAsync(ct);
        await Backend.DeleteCredentials(ct);
        UpdateState(new CredentialModel
        {
            State = CredentialState.Invalid,
        });
    }

    private async Task<CredentialModel> LoadCredentialsInner(CancellationToken ct)
    {
        CredentialModel model;
        try
        {
            var raw = await Backend.ReadCredentials(ct);
            model = await PopulateModel(raw, ct);
        }
        catch
        {
            // This catch will be hit if a SetCredentials operation started, or
            // if the read/populate failed for some other reason (e.g. HTTP
            // timeout).
            //
            // We don't need to clear the credentials here, the app will think
            // they're unset and any subsequent SetCredentials call after the
            // user signs in again will overwrite the old invalid ones.
            model = new CredentialModel
            {
                State = CredentialState.Invalid,
            };
        }

        // Grab the lock again so we can update the state. If we got cancelled
        // due to a SetCredentials call, _latestCredentials will be populated so
        // we just return that instead.
        using (await _opLock.LockAsync(ct))
        {
            var latestCreds = _latestCredentials;
            if (latestCreds != null) return latestCreds;
            if (_loadCts != null)
            {
                _loadCts.Dispose();
                _loadCts = null;
                _inFlightLoad = null;
            }

            UpdateState(model);
            return model;
        }
    }

    private async Task<CredentialModel> PopulateModel(RawCredentials? credentials, CancellationToken ct)
    {
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.CoderUrl) ||
            string.IsNullOrWhiteSpace(credentials.ApiToken))
            return new CredentialModel
            {
                State = CredentialState.Invalid,
            };

        BuildInfo buildInfo;
        User me;
        try
        {
            var sdkClient = CoderApiClientFactory.Create(credentials.CoderUrl);
            // BuildInfo does not require authentication.
            buildInfo = await sdkClient.GetBuildInfo(ct);
            sdkClient.SetSessionToken(credentials.ApiToken);
            me = await sdkClient.GetUser(User.Me, ct);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Could not connect to or verify Coder server", e);
        }

        ServerVersionUtilities.ParseAndValidateServerVersion(buildInfo.Version);
        if (string.IsNullOrWhiteSpace(me.Username))
            throw new InvalidOperationException("Could not retrieve user information, username is empty");

        return new CredentialModel
        {
            State = CredentialState.Valid,
            CoderUrl = credentials.CoderUrl,
            ApiToken = credentials.ApiToken,
            Username = me.Username,
        };
    }

    // Lock must be held when calling this function.
    private void UpdateState(CredentialModel newModel)
    {
        _latestCredentials = newModel;
        CredentialsChanged?.Invoke(this, newModel.Clone());
    }
}

public class WindowsCredentialBackend : ICredentialBackend
{
    private readonly string _credentialsTargetName;

    public WindowsCredentialBackend(string credentialsTargetName)
    {
        _credentialsTargetName = credentialsTargetName;
    }

    public Task<RawCredentials?> ReadCredentials(CancellationToken ct = default)
    {
        var raw = NativeApi.ReadCredentials(_credentialsTargetName);
        if (raw == null) return Task.FromResult<RawCredentials?>(null);

        RawCredentials? credentials;
        try
        {
            credentials = JsonSerializer.Deserialize(raw, RawCredentialsJsonContext.Default.RawCredentials);
        }
        catch (JsonException)
        {
            credentials = null;
        }

        return Task.FromResult(credentials);
    }

    public Task WriteCredentials(RawCredentials credentials, CancellationToken ct = default)
    {
        var raw = JsonSerializer.Serialize(credentials, RawCredentialsJsonContext.Default.RawCredentials);
        NativeApi.WriteCredentials(_credentialsTargetName, raw);
        return Task.CompletedTask;
    }

    public Task DeleteCredentials(CancellationToken ct = default)
    {
        NativeApi.DeleteCredentials(_credentialsTargetName);
        return Task.CompletedTask;
    }

    private static class NativeApi
    {
        private const int CredentialTypeGeneric = 1;
        private const int PersistenceTypeLocalComputer = 2;
        private const int ErrorNotFound = 1168;
        private const int CredMaxCredentialBlobSize = 5 * 512;

        public static string? ReadCredentials(string targetName)
        {
            if (!CredReadW(targetName, CredentialTypeGeneric, 0, out var credentialPtr))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return null;
                throw new InvalidOperationException($"Failed to read credentials (Error {error})");
            }

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                return Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / sizeof(char));
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        public static void WriteCredentials(string targetName, string secret)
        {
            var byteCount = Encoding.Unicode.GetByteCount(secret);
            if (byteCount > CredMaxCredentialBlobSize)
                throw new ArgumentOutOfRangeException(nameof(secret),
                    $"The secret is greater than {CredMaxCredentialBlobSize} bytes");

            var credentialBlob = Marshal.StringToHGlobalUni(secret);
            var cred = new CREDENTIAL
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = byteCount,
                CredentialBlob = credentialBlob,
                Persist = PersistenceTypeLocalComputer,
            };
            try
            {
                if (!CredWriteW(ref cred, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Failed to write credentials (Error {error})");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(credentialBlob);
            }
        }

        public static void DeleteCredentials(string targetName)
        {
            if (!CredDeleteW(targetName, CredentialTypeGeneric, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return;
                throw new InvalidOperationException($"Failed to delete credentials (Error {error})");
            }
        }

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW([In] ref CREDENTIAL userCredential, [In] uint flags);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree([In] IntPtr cred);

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDeleteW(string target, int type, int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;

            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;

            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;

            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;

            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;

            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.Vpn.Utilities;
using CoderSdk;

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
    public string? GetSignInUri();

    /// <summary>
    ///     Returns cached credentials or loads/verifies them from storage if not cached.
    /// </summary>
    public Task<CredentialModel> LoadCredentials(CancellationToken ct = default);

    public Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct = default);

    public void ClearCredentials();
}

public class CredentialManager : ICredentialManager
{
    private const string CredentialsTargetName = "Coder.Desktop.App.Credentials";

    private readonly RaiiSemaphoreSlim _loadLock = new(1, 1);
    private readonly RaiiSemaphoreSlim _stateLock = new(1, 1);
    private CredentialModel? _latestCredentials;

    public event EventHandler<CredentialModel>? CredentialsChanged;

    public CredentialModel GetCachedCredentials()
    {
        using var _ = _stateLock.Lock();
        if (_latestCredentials != null) return _latestCredentials.Clone();

        return new CredentialModel
        {
            State = CredentialState.Unknown,
        };
    }

    public string? GetSignInUri()
    {
        try
        {
            var raw = ReadCredentials();
            if (raw is not null && !string.IsNullOrWhiteSpace(raw.CoderUrl)) return raw.CoderUrl;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    public async Task<CredentialModel> LoadCredentials(CancellationToken ct = default)
    {
        using var _ = await _loadLock.LockAsync(ct);
        using (await _stateLock.LockAsync(ct))
        {
            if (_latestCredentials != null) return _latestCredentials.Clone();
        }

        CredentialModel model;
        try
        {
            var raw = ReadCredentials();
            model = await PopulateModel(raw, ct);
        }
        catch (Exception e)
        {
            // We don't need to clear the credentials here, the app will think
            // they're unset and any subsequent SetCredentials call after the
            // user signs in again will overwrite the old invalid ones.
            model = new CredentialModel
            {
                State = CredentialState.Invalid,
            };
        }

        UpdateState(model.Clone());
        return model.Clone();
    }

    public async Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(coderUrl)) throw new ArgumentException("Coder URL is required", nameof(coderUrl));
        coderUrl = coderUrl.Trim();
        if (coderUrl.Length > 128) throw new ArgumentOutOfRangeException(nameof(coderUrl), "Coder URL is too long");
        if (!Uri.TryCreate(coderUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Coder URL '{coderUrl}' is not a valid URL", nameof(coderUrl));
        if (uri.PathAndQuery != "/") throw new ArgumentException("Coder URL must be the root URL", nameof(coderUrl));
        if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("API token is required", nameof(apiToken));
        apiToken = apiToken.Trim();

        var raw = new RawCredentials
        {
            CoderUrl = coderUrl,
            ApiToken = apiToken,
        };
        var model = await PopulateModel(raw, ct);
        WriteCredentials(raw);
        UpdateState(model);
    }

    public void ClearCredentials()
    {
        NativeApi.DeleteCredentials(CredentialsTargetName);
        UpdateState(new CredentialModel
        {
            State = CredentialState.Invalid,
        });
    }

    private async Task<CredentialModel> PopulateModel(RawCredentials? credentials, CancellationToken ct = default)
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
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var sdkClient = new CoderApiClient(credentials.CoderUrl);
            sdkClient.SetSessionToken(credentials.ApiToken);
            buildInfo = await sdkClient.GetBuildInfo(cts.Token);
            me = await sdkClient.GetUser(User.Me, cts.Token);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Could not connect to or verify Coder server", e);
        }

        ServerVersionUtilities.ParseAndValidateServerVersion(buildInfo.Version);
        return new CredentialModel
        {
            State = CredentialState.Valid,
            CoderUrl = credentials.CoderUrl,
            ApiToken = credentials.ApiToken,
            Username = me.Username,
        };
    }

    private void UpdateState(CredentialModel newModel)
    {
        using (_stateLock.Lock())
        {
            _latestCredentials = newModel.Clone();
        }

        CredentialsChanged?.Invoke(this, newModel.Clone());
    }

    private static RawCredentials? ReadCredentials()
    {
        var raw = NativeApi.ReadCredentials(CredentialsTargetName);
        if (raw == null) return null;

        RawCredentials? credentials;
        try
        {
            credentials = JsonSerializer.Deserialize(raw, RawCredentialsJsonContext.Default.RawCredentials);
        }
        catch (JsonException)
        {
            return null;
        }

        if (credentials is null || string.IsNullOrWhiteSpace(credentials.CoderUrl) ||
            string.IsNullOrWhiteSpace(credentials.ApiToken)) return null;

        return credentials;
    }

    private static void WriteCredentials(RawCredentials credentials)
    {
        var raw = JsonSerializer.Serialize(credentials, RawCredentialsJsonContext.Default.RawCredentials);
        NativeApi.WriteCredentials(CredentialsTargetName, raw);
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

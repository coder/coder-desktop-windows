using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.Vpn.Utilities;
using CoderSdk;

namespace Coder.Desktop.App.Services;

public interface ICredentialManager
{
    public event EventHandler<CredentialModel> CredentialsChanged;

    public CredentialModel GetCredentials();

    public Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct = default);

    public void ClearCredentials();
}

public class CredentialManager : ICredentialManager
{
    private const string CredentialsTargetName = "Coder.Desktop.App.Credentials";

    private readonly RaiiSemaphoreSlim _lock = new(1, 1);
    private CredentialModel? _latestCredentials;

    public event EventHandler<CredentialModel>? CredentialsChanged;

    public CredentialModel GetCredentials()
    {
        using var _ = _lock.Lock();
        if (_latestCredentials != null) return _latestCredentials.Clone();

        var rawCredentials = ReadCredentials();
        if (rawCredentials is null)
            _latestCredentials = new CredentialModel
            {
                State = CredentialState.Invalid,
            };
        else
            _latestCredentials = new CredentialModel
            {
                State = CredentialState.Valid,
                CoderUrl = rawCredentials.CoderUrl,
                ApiToken = rawCredentials.ApiToken,
            };
        return _latestCredentials.Clone();
    }

    public async Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(coderUrl)) throw new ArgumentException("Coder URL is required", nameof(coderUrl));
        coderUrl = coderUrl.Trim();
        if (coderUrl.Length > 128) throw new ArgumentOutOfRangeException(nameof(coderUrl), "Coder URL is too long");
        if (!Uri.TryCreate(coderUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Coder URL '{coderUrl}' is not a valid URL", nameof(coderUrl));
        if (uri.Scheme != "https") throw new ArgumentException("Coder URL must use HTTPS", nameof(coderUrl));
        if (uri.PathAndQuery != "/") throw new ArgumentException("Coder URL must be the root URL", nameof(coderUrl));
        if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("API token is required", nameof(apiToken));
        apiToken = apiToken.Trim();
        if (apiToken.Length != 33)
            throw new ArgumentOutOfRangeException(nameof(apiToken), "API token must be 33 characters long");

        try
        {
            var sdkClient = new CoderApiClient(uri);
            // TODO: we should probably perform a version check here too,
            // rather than letting the service do it on Start
            _ = await sdkClient.GetBuildInfo(ct);
            _ = await sdkClient.GetUser(User.Me, ct);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Could not connect to or verify Coder server", e);
        }

        WriteCredentials(new RawCredentials
        {
            CoderUrl = coderUrl,
            ApiToken = apiToken,
        });

        UpdateState(new CredentialModel
        {
            State = CredentialState.Valid,
            CoderUrl = coderUrl,
            ApiToken = apiToken,
        });
    }

    public void ClearCredentials()
    {
        NativeApi.DeleteCredentials(CredentialsTargetName);
        UpdateState(new CredentialModel
        {
            State = CredentialState.Invalid,
            CoderUrl = null,
            ApiToken = null,
        });
    }

    private void UpdateState(CredentialModel newModel)
    {
        _latestCredentials = newModel;
        CredentialsChanged?.Invoke(this, _latestCredentials.Clone());
    }

    private RawCredentials? ReadCredentials()
    {
        var raw = NativeApi.ReadCredentials(CredentialsTargetName);
        if (raw == null) return null;

        RawCredentials? credentials;
        try
        {
            credentials = JsonSerializer.Deserialize<RawCredentials>(raw);
        }
        catch (JsonException)
        {
            return null;
        }

        if (credentials is null || string.IsNullOrWhiteSpace(credentials.CoderUrl) ||
            string.IsNullOrWhiteSpace(credentials.ApiToken)) return null;

        return credentials;
    }

    private void WriteCredentials(RawCredentials credentials)
    {
        var raw = JsonSerializer.Serialize(credentials);
        NativeApi.WriteCredentials(CredentialsTargetName, raw);
    }

    private class RawCredentials
    {
        public required string CoderUrl { get; set; }
        public required string ApiToken { get; set; }
    }

    private static class NativeApi
    {
        private const int CredentialTypeGeneric = 1;
        private const int PersistenceTypeLocalComputer = 2;
        private const int ErrorNotFound = 1168;

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
            if (Encoding.Unicode.GetByteCount(secret) > 512)
                throw new ArgumentOutOfRangeException(nameof(secret), "The secret is greater than 512 bytes");

            var credentialBlob = Marshal.StringToHGlobalUni(secret);
            var cred = new CREDENTIAL
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = secret.Length * sizeof(char),
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

            [MarshalAs(UnmanagedType.LPWStr)]
            public string TargetName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Comment;

            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string TargetAlias;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string UserName;
        }
    }
}

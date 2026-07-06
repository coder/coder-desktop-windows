using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Coder.Desktop.App.Services;

public class WindowsCredentialBackend : ICredentialBackend
{
    public const string CoderCredentialsTargetName = "Coder.Desktop.App.Credentials";

    private readonly string _credentialsTargetName;

    public WindowsCredentialBackend(string credentialsTargetName)
    {
        _credentialsTargetName = credentialsTargetName;
    }

    public Task<RawCredentials?> ReadCredentials(CancellationToken ct = default)
    {
        var raw = Wincred.ReadCredentials(_credentialsTargetName);
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
        Wincred.WriteCredentials(_credentialsTargetName, raw);
        return Task.CompletedTask;
    }

    public Task DeleteCredentials(CancellationToken ct = default)
    {
        Wincred.DeleteCredentials(_credentialsTargetName);
        return Task.CompletedTask;
    }

}

/// <summary>
/// Wincred provides relatively low level wrapped calls to the Wincred.h native API.
/// </summary>
internal static class Wincred
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialTypeDomainPassword = 2;
    private const int PersistenceTypeLocalComputer = 2;
    private const int ErrorNotFound = 1168;
    private const int CredMaxCredentialBlobSize = 5 * 512;
    private const string PackageNTLM = "NTLM";

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
            var cred = Marshal.PtrToStructure<CREDENTIALW>(credentialPtr);
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
        var cred = new CREDENTIALW
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

    public static void WriteDomainCredentials(string domainName, string serverName, string username, string password)
    {
        var targetName = $"{domainName}/{serverName}";
        var targetInfo = new CREDENTIAL_TARGET_INFORMATIONW
        {
            TargetName = targetName,
            DnsServerName = serverName,
            DnsDomainName = domainName,
            PackageName = PackageNTLM,
        };
        var byteCount = Encoding.Unicode.GetByteCount(password);
        if (byteCount > CredMaxCredentialBlobSize)
            throw new ArgumentOutOfRangeException(nameof(password),
                $"The secret is greater than {CredMaxCredentialBlobSize} bytes");

        var credentialBlob = Marshal.StringToHGlobalUni(password);
        var cred = new CREDENTIALW
        {
            Type = CredentialTypeDomainPassword,
            TargetName = targetName,
            CredentialBlobSize = byteCount,
            CredentialBlob = credentialBlob,
            Persist = PersistenceTypeLocalComputer,
            UserName = username,
        };
        try
        {
            if (!CredWriteDomainCredentialsW(ref targetInfo, ref cred, 0))
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

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW([In] ref CREDENTIALW userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr cred);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteDomainCredentialsW([In] ref CREDENTIAL_TARGET_INFORMATIONW target, [In] ref CREDENTIALW userCredential, [In] uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct CREDENTIALW
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

    [StructLayout(LayoutKind.Sequential)]
    private struct CREDENTIAL_TARGET_INFORMATIONW
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string NetbiosServerName;
        [MarshalAs(UnmanagedType.LPWStr)] public string DnsServerName;
        [MarshalAs(UnmanagedType.LPWStr)] public string NetbiosDomainName;
        [MarshalAs(UnmanagedType.LPWStr)] public string DnsDomainName;
        [MarshalAs(UnmanagedType.LPWStr)] public string DnsTreeName;
        [MarshalAs(UnmanagedType.LPWStr)] public string PackageName;

        public uint Flags;
        public uint CredTypeCount;
        public IntPtr CredTypes;
    }
}

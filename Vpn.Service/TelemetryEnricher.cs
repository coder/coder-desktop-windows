using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.Vpn.Service;

public interface ITelemetryEnricher
{
    public StartRequest EnrichStartRequest(StartRequest original);
}

public class TelemetryEnricher : ITelemetryEnricher
{
    private readonly string? _version;
    private readonly string? _deviceID;

    public TelemetryEnricher()
    {
        var assembly = Assembly.GetExecutingAssembly();
        _version = assembly.GetName().Version?.ToString();
        _deviceID = GetDeviceId();
    }

    public StartRequest EnrichStartRequest(StartRequest original)
    {
        var req = original.Clone();
        req.DeviceOs = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        if (_version != null) req.CoderDesktopVersion = _version;
        if (_deviceID != null) req.DeviceId = _deviceID;
        return req;
    }

    private static string? GetDeviceId()
    {
        try
        {
            string? rawId = null;

            if (OperatingSystem.IsWindows())
            {
                rawId = GetWindowsDeviceId();
            }
            else if (OperatingSystem.IsLinux())
            {
                // /etc/machine-id is standard on systemd systems
                const string machineIdPath = "/etc/machine-id";
                if (File.Exists(machineIdPath))
                    rawId = File.ReadAllText(machineIdPath).Trim();
            }

            if (string.IsNullOrEmpty(rawId))
                return null;

            var idBytes = Encoding.UTF8.GetBytes(rawId);
            var hash = SHA256.HashData(idBytes);
            return Convert.ToBase64String(hash);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsDeviceId()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SQMClient");
        return key?.GetValue("MachineId") as string;
    }
}

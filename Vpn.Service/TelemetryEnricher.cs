using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Coder.Desktop.Vpn.Proto;
using Microsoft.Win32;

namespace Coder.Desktop.Vpn.Service;

// <summary>
// ITelemetryEnricher contains methods for enriching messages with telemetry
// information
// </summary>
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

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SQMClient");
        if (key != null)
        {
            // this is the "Device ID" shown in settings. I don't think it's personally
            // identifiable, but let's hash it just to be sure.
            var deviceID = key.GetValue("MachineId") as string;
            if (!string.IsNullOrEmpty(deviceID))
            {
                var idBytes = Encoding.UTF8.GetBytes(deviceID);
                var hash = SHA256.HashData(idBytes);
                _deviceID = Convert.ToBase64String(hash);
            }
        }
    }

    public StartRequest EnrichStartRequest(StartRequest original)
    {
        var req = original.Clone();
        req.DeviceOs = "Windows";
        if (_version != null) req.CoderDesktopVersion = _version;
        if (_deviceID != null) req.DeviceId = _deviceID;
        return req;
    }
}

using System.Diagnostics;

namespace Coder.Desktop.App.Services;

/// <summary>
/// RDP connector for Linux using xfreerdp or remmina.
/// </summary>
public class LinuxRdpConnector : IRdpConnector
{
    private RdpCredentials? _lastCredentials;
    private string? _lastFqdn;

    public void WriteCredentials(string fqdn, RdpCredentials credentials)
    {
        // Store temporarily — xfreerdp accepts credentials as command-line args
        _lastFqdn = fqdn;
        _lastCredentials = credentials;
    }

    public Task Connect(string fqdn, int port = IRdpConnector.DefaultPort, CancellationToken ct = default)
    {
        // Try xfreerdp3 first, then xfreerdp, then remmina
        string? exe = null;
        foreach (var candidate in new[] { "/usr/bin/xfreerdp3", "/usr/bin/xfreerdp", "/usr/bin/remmina" })
        {
            if (File.Exists(candidate))
            {
                exe = candidate;
                break;
            }
        }

        if (exe == null)
            throw new FileNotFoundException(
                "No RDP client found. Please install xfreerdp or remmina.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
        };

        if (exe.Contains("xfreerdp"))
        {
            psi.ArgumentList.Add($"/v:{fqdn}:{port}");
            psi.ArgumentList.Add("/dynamic-resolution");
            psi.ArgumentList.Add("+clipboard");

            if (_lastCredentials.HasValue && _lastFqdn == fqdn)
            {
                var lastCredentials = _lastCredentials.Value;
                if (!string.IsNullOrEmpty(lastCredentials.Username))
                    psi.ArgumentList.Add($"/u:{lastCredentials.Username}");
                if (!string.IsNullOrEmpty(lastCredentials.Password))
                    psi.ArgumentList.Add($"/p:{lastCredentials.Password}");
            }
        }
        else
        {
            // remmina
            psi.ArgumentList.Add($"--connect=rdp://{fqdn}:{port}");
        }

        Process.Start(psi);
        return Task.CompletedTask;
    }
}

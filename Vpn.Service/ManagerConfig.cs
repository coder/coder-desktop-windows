using System.ComponentModel.DataAnnotations;

namespace Coder.Desktop.Vpn.Service;

public class ManagerConfig
{
    [Required]
    [RegularExpression(@"^([a-zA-Z0-9_-]+\.)*[a-zA-Z0-9_-]+$")]
    public string ServiceRpcPipeName { get; set; } = "Coder.Desktop.Vpn";

    /// <summary>
    /// Path to the Unix domain socket for RPC (Linux only).
    /// If empty, defaults to /run/coder-desktop/vpn.sock.
    /// </summary>
    public string ServiceRpcSocketPath { get; set; } = "";

    [Required] public string TunnelBinaryPath { get; set; } = OperatingSystem.IsWindows()
        ? @"C:\coder-vpn.exe"
        : "/usr/lib/coder-desktop/coder-vpn";

    // If empty, signatures will not be verified.
    [Required] public string TunnelBinarySignatureSigner { get; set; } = OperatingSystem.IsWindows()
        ? "Coder Technologies Inc."
        : ""; // No Authenticode on Linux

    [Required] public bool TunnelBinaryAllowVersionMismatch { get; set; } = false;
}

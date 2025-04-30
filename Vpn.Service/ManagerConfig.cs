using System.ComponentModel.DataAnnotations;

namespace Coder.Desktop.Vpn.Service;

// These values are the config option names used in the registry. Any option
// here can be configured with `(Debug)?Manager:OptionName` in the registry.
//
// They should not be changed without backwards compatibility considerations.
// If changed here, they should also be changed in the installer.
public class ManagerConfig
{
    [Required]
    [RegularExpression(@"^([a-zA-Z0-9_-]+\.)*[a-zA-Z0-9_-]+$")]
    public string ServiceRpcPipeName { get; set; } = "Coder.Desktop.Vpn";

    [Required] public string TunnelBinaryPath { get; set; } = @"C:\coder-vpn.exe";

    // If empty, signatures will not be verified.
    [Required] public string TunnelBinarySignatureSigner { get; set; } = "Coder Technologies Inc.";

    [Required] public bool TunnelBinaryAllowVersionMismatch { get; set; } = false;
}

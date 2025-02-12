using System.ComponentModel.DataAnnotations;

namespace Coder.Desktop.Vpn.Service;

public class ManagerConfig
{
    [Required]
    [RegularExpression(@"^([a-zA-Z0-9_-]+\.)*[a-zA-Z0-9_-]+$")]
    public string ServiceRpcPipeName { get; set; } = "Coder.Desktop.Vpn";

    [Required]
    public string TunnelBinaryPath { get; set; } = @"C:\coder-vpn.exe";

    [Required]
    public string LogFileLocation { get; set; } = @"C:\coder-desktop-service.log";
}

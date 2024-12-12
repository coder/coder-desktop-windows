using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Coder.Desktop.Vpn.Service;

[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class ManagerConfig
{
    [Required]
    [RegularExpression(@"^([a-zA-Z0-9_-]+\.)*[a-zA-Z0-9_-]+$")]
    public string ServiceRpcPipeName { get; set; } = "Coder.Desktop.Vpn";

    // TODO: pick a better default path
    [Required]
    public string TunnelBinaryPath { get; set; } = @"C:\coder-vpn.exe";
}

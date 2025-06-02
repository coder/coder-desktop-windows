using System.Collections.Generic;
using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.App.Models;

public enum RpcLifecycle
{
    Disconnected,
    Connecting,
    Connected,
}

public enum VpnLifecycle
{
    Unknown,
    Stopped,
    Starting,
    Started,
    Stopping,
}

public class VpnStartupProgress
{
    public double Progress { get; set; } = 0.0; // 0.0 to 1.0
    public string Message { get; set; } = string.Empty;

    public VpnStartupProgress Clone()
    {
        return new VpnStartupProgress
        {
            Progress = Progress,
            Message = Message,
        };
    }
}

public class RpcModel
{
    public RpcLifecycle RpcLifecycle { get; set; } = RpcLifecycle.Disconnected;

    public VpnLifecycle VpnLifecycle { get; set; } = VpnLifecycle.Unknown;

    // Nullable because it is only set when the VpnLifecycle is Starting
    public VpnStartupProgress? VpnStartupProgress { get; set; }

    public IReadOnlyList<Workspace> Workspaces { get; set; } = [];

    public IReadOnlyList<Agent> Agents { get; set; } = [];

    public RpcModel Clone()
    {
        return new RpcModel
        {
            RpcLifecycle = RpcLifecycle,
            VpnLifecycle = VpnLifecycle,
            VpnStartupProgress = VpnStartupProgress?.Clone(),
            Workspaces = Workspaces,
            Agents = Agents,
        };
    }
}

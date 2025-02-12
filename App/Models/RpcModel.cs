using System.Collections.Generic;
using System.Linq;
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

public class RpcModel
{
    public RpcLifecycle RpcLifecycle { get; set; } = RpcLifecycle.Disconnected;

    public VpnLifecycle VpnLifecycle { get; set; } = VpnLifecycle.Unknown;

    public List<Workspace> Workspaces { get; set; } = [];

    public List<Agent> Agents { get; set; } = [];

    public RpcModel Clone()
    {
        return new RpcModel
        {
            RpcLifecycle = RpcLifecycle,
            VpnLifecycle = VpnLifecycle,
            Workspaces = Workspaces.ToList(),
            Agents = Agents.ToList(),
        };
    }
}

using System.Collections.Generic;

namespace Coder.Desktop.App.Models;

public enum RpcLifecycle
{
    Disconnected,
    Connecting,
    Connected,
}

public enum VpnLifecycle
{
    Stopped,
    Starting,
    Started,
    Stopping,
}

public class RpcModel
{
    public RpcLifecycle RpcLifecycle { get; set; } = RpcLifecycle.Disconnected;

    public VpnLifecycle VpnLifecycle { get; set; } = VpnLifecycle.Stopped;

    public List<object> Agents { get; set; } = [];

    public RpcModel Clone()
    {
        return new RpcModel
        {
            RpcLifecycle = RpcLifecycle,
            VpnLifecycle = VpnLifecycle,
            Agents = Agents,
        };
    }
}

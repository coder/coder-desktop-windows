using Coder.Desktop.App.Models;

namespace Coder.Desktop.App.Services;

public class RpcOperationException : Exception
{
    public RpcOperationException(string message, Exception innerException) : base(message, innerException) { }
    public RpcOperationException(string message) : base(message) { }
}

public class VpnLifecycleException : Exception
{
    public VpnLifecycleException(string message, Exception innerException) : base(message, innerException) { }
    public VpnLifecycleException(string message) : base(message) { }
}

public interface IRpcController : IAsyncDisposable
{
    event EventHandler<RpcModel> StateChanged;
    RpcModel GetState();
    Task Reconnect(CancellationToken ct = default);
    Task StartVpn(CancellationToken ct = default);
    Task StopVpn(CancellationToken ct = default);
}

using Microsoft.Extensions.Hosting;

namespace Coder.Desktop.Vpn.Service;

public class ManagerRpcService : BackgroundService
{
    private readonly IManagerRpc _managerRpc;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ManagerRpcService(IManagerRpc managerRpc)
    {
        _managerRpc = managerRpc;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _managerRpc.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _managerRpc.ExecuteAsync(stoppingToken);
    }
}

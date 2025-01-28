using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.Vpn.Service;

/// <summary>
///     Wraps Manager to provide a BackgroundService that informs the singleton Manager to shut down when stop is
///     requested.
/// </summary>
public class ManagerService : BackgroundService
{
    private readonly ILogger<ManagerService> _logger;
    private readonly IManager _manager;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ManagerService(ILogger<ManagerService> logger, IManager manager)
    {
        _logger = logger;
        _manager = manager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Block until the service is stopped.
        await Task.Delay(-1, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Informing Manager to stop");
        await _manager.StopAsync(cancellationToken);
    }
}

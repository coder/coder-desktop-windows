using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Coder.Desktop.Vpn.Service;

/// <summary>
///     Watches for the system resuming from sleep and notifies the manager so it can prompt the tunnel to re-discover
///     network paths.
/// </summary>
public class SystemResumeMonitor : IHostedService
{
    private readonly ILogger<SystemResumeMonitor> _logger;
    private readonly IManager _manager;

    // ReSharper disable once ConvertToPrimaryConstructor
    public SystemResumeMonitor(ILogger<SystemResumeMonitor> logger, IManager manager)
    {
        _logger = logger;
        _manager = manager;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // SystemEvents delivers power notifications on a dedicated broadcast
        // thread, so no message pump is required in this process.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        return Task.CompletedTask;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _logger.LogInformation("System resumed from sleep, notifying manager");
        // Handle the resume in the background to avoid blocking the shared
        // SystemEvents broadcast thread. HandleSystemResume logs and swallows
        // all send failures.
        _ = Task.Run(() => _manager.HandleSystemResume());
    }
}

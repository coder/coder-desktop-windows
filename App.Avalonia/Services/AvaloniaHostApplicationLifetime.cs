using Microsoft.Extensions.Hosting;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Lightweight <see cref="IHostApplicationLifetime"/> adapter for Avalonia apps
/// that are not running inside a generic host.
/// </summary>
public sealed class AvaloniaHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly Action _stopApplication;
    private readonly CancellationTokenSource _startedCts = new();
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly CancellationTokenSource _stoppedCts = new();

    private int _stopRequested;

    public AvaloniaHostApplicationLifetime(Action stopApplication)
    {
        _stopApplication = stopApplication;
    }

    public CancellationToken ApplicationStarted => _startedCts.Token;
    public CancellationToken ApplicationStopping => _stoppingCts.Token;
    public CancellationToken ApplicationStopped => _stoppedCts.Token;

    public void NotifyStarted()
    {
        if (!_startedCts.IsCancellationRequested)
            _startedCts.Cancel();
    }

    public void NotifyStopped()
    {
        if (!_stoppedCts.IsCancellationRequested)
            _stoppedCts.Cancel();
    }

    public void StopApplication()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
            return;

        if (!_stoppingCts.IsCancellationRequested)
            _stoppingCts.Cancel();

        try
        {
            _stopApplication();
        }
        finally
        {
            if (!_stoppedCts.IsCancellationRequested)
                _stoppedCts.Cancel();
        }
    }

    public void Dispose()
    {
        _startedCts.Dispose();
        _stoppingCts.Dispose();
        _stoppedCts.Dispose();
    }
}

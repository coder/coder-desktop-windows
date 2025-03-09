namespace Coder.Desktop.Vpn.Utilities;

/// <summary>
///     RaiiSemaphoreSlim is a wrapper around SemaphoreSlim that provides RAII-style locking.
/// </summary>
public class RaiiSemaphoreSlim : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public RaiiSemaphoreSlim(int initialCount, int maxCount)
    {
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    public IDisposable Lock()
    {
        _semaphore.Wait();
        return new Locker(_semaphore);
    }

    public IDisposable? Lock(TimeSpan timeout)
    {
        if (!_semaphore.Wait(timeout)) return null;
        return new Locker(_semaphore);
    }

    public async Task<IDisposable> LockAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Locker(_semaphore);
    }

    public async Task<IDisposable?> LockAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!await _semaphore.WaitAsync(timeout, ct)) return null;
        return new Locker(_semaphore);
    }

    private class Locker : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Locker(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            _semaphore.Release();
            GC.SuppressFinalize(this);
        }
    }
}

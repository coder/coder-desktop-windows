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

    public async ValueTask<IDisposable> LockAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Locker(_semaphore);
    }

    public async ValueTask<IDisposable?> LockAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!await _semaphore.WaitAsync(timeout, ct)) return null;
        return new Locker(_semaphore);
    }

    private class Locker : IDisposable
    {
        private readonly SemaphoreSlim _semaphore1;

        public Locker(SemaphoreSlim semaphore)
        {
            _semaphore1 = semaphore;
        }

        public void Dispose()
        {
            _semaphore1.Release();
            GC.SuppressFinalize(this);
        }
    }
}

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

    public async ValueTask<IDisposable> LockAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Lock(_semaphore);
    }

    private class Lock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore1;

        public Lock(SemaphoreSlim semaphore)
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

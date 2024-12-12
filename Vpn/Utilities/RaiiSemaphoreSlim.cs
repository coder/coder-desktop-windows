namespace Coder.Desktop.Vpn.Utilities;

/// <summary>
///     RaiiSemaphoreSlim is a wrapper around SemaphoreSlim that provides RAII-style locking.
/// </summary>
public class RaiiSemaphoreSlim(int initialCount, int maxCount) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(initialCount, maxCount);

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

    private class Lock(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose()
        {
            semaphore.Release();
            GC.SuppressFinalize(this);
        }
    }
}

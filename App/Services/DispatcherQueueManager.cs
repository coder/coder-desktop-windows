using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Coder.Desktop.App.Services;

public interface IDispatcherQueueManager
{
    public void RunInUiThread(DispatcherQueueHandler action);
}

public class AppDispatcherQueueManager : IDispatcherQueueManager
{
    private static DispatcherQueue? DispatcherQueue =>
        ((App)Application.Current).TrayWindow?.DispatcherQueue;

    public void RunInUiThread(DispatcherQueueHandler action)
    {
        var dispatcherQueue = DispatcherQueue;
        if (dispatcherQueue is null)
            throw new InvalidOperationException("DispatcherQueue is not available");
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }
        dispatcherQueue.TryEnqueue(action);
    }
}

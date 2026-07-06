using System;
using Microsoft.UI.Dispatching;

namespace Coder.Desktop.App.Services;

/// <summary>
///     WinUI implementation of IDispatcher backed by a DispatcherQueue.
/// </summary>
public class WinUIDispatcher : IDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUIDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public bool CheckAccess()
    {
        return _dispatcherQueue.HasThreadAccess;
    }

    public void Post(Action action)
    {
        _dispatcherQueue.TryEnqueue(() => action());
    }
}

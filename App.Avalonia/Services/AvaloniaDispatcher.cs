using Avalonia.Threading;
using IDispatcher = Coder.Desktop.App.Services.IDispatcher;

namespace Coder.Desktop.App;

public class AvaloniaDispatcher : IDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    /// <summary>
    /// Always post to the dispatcher queue, even when already on the UI thread.
    /// This avoids subtle reentrancy issues when event-handler chains call
    /// MutateState → StateChanged → UpdateFromRpcModel inline.
    /// </summary>
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}

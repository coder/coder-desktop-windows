using Avalonia.Threading;
using IDispatcher = Coder.Desktop.App.Services.IDispatcher;

namespace Coder.Desktop.App;

public class AvaloniaDispatcher : IDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}

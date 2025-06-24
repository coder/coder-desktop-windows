using Microsoft.UI.Dispatching;

namespace Coder.Desktop.App.Services;

public interface IDispatcherQueueManager
{
    public void RunInUiThread(DispatcherQueueHandler action);
}

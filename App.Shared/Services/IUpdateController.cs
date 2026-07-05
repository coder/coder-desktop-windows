namespace Coder.Desktop.App.Services;

public interface IUpdateController : IAsyncDisposable
{
    Task CheckForUpdatesNow();
}

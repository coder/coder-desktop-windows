namespace Coder.Desktop.App.Services;

/// <summary>
/// Abstracts UI thread dispatching. Replaces WinUI's DispatcherQueue.
/// </summary>
public interface IDispatcher
{
    /// <summary>Whether the calling thread is the UI thread.</summary>
    bool CheckAccess();

    /// <summary>Post an action to run on the UI thread.</summary>
    void Post(Action action);
}

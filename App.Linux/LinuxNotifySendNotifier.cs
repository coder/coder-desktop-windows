using System.Diagnostics;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Linux notification implementation using notify-send.
/// </summary>
public class LinuxNotifySendNotifier : IUserNotifier
{
    private readonly Dictionary<string, INotificationHandler> _handlers = new();

    public void RegisterHandler(string name, INotificationHandler handler)
    {
        _handlers[name] = handler;
    }

    public void UnregisterHandler(string name)
    {
        _handlers.Remove(name);
    }

    public Task ShowErrorNotification(string title, string message, CancellationToken ct = default)
    {
        try
        {
            Process.Start("notify-send", [title, message, "--app-name=Coder Desktop", "--urgency=critical"]);
        }
        catch
        {
            // notify-send may not be available — fail silently
        }
        return Task.CompletedTask;
    }

    public Task ShowActionNotification(string title, string message, string? handlerName,
        IDictionary<string, string>? args = null, CancellationToken ct = default)
    {
        try
        {
            Process.Start("notify-send", [title, message, "--app-name=Coder Desktop"]);
        }
        catch
        {
            // fail silently
        }
        return Task.CompletedTask;
    }

    public void HandleNotificationActivation(IDictionary<string, string> args)
    {
        // notify-send doesn't support action callbacks
    }

    public ValueTask DisposeAsync()
    {
        _handlers.Clear();
        return ValueTask.CompletedTask;
    }
}

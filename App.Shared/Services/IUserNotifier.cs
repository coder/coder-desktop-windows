namespace Coder.Desktop.App.Services;

public interface INotificationHandler
{
    void HandleNotificationActivation(IDictionary<string, string> args);
}

public interface IDefaultNotificationHandler : INotificationHandler
{
}

public interface IUserNotifier : INotificationHandler, IAsyncDisposable
{
    void RegisterHandler(string name, INotificationHandler handler);
    void UnregisterHandler(string name);
    Task ShowErrorNotification(string title, string message, CancellationToken ct = default);
    Task ShowActionNotification(string title, string message, string? handlerName,
        IDictionary<string, string>? args = null, CancellationToken ct = default);
}

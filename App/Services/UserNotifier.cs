using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Coder.Desktop.App.Services;

public interface INotificationHandler
{
    public void HandleNotificationActivation(IDictionary<string, string> args);
}

public interface IUserNotifier : INotificationHandler, IAsyncDisposable
{
    public void RegisterHandler(string name, INotificationHandler handler);

    public Task ShowErrorNotification(string title, string message, CancellationToken ct = default);
    public Task ShowActionNotification(string title, string message, string handlerName, IDictionary<string, string>? args = null, CancellationToken ct = default);
}

public class UserNotifier(ILogger<UserNotifier> logger, IDispatcherQueueManager dispatcherQueueManager) : IUserNotifier
{
    private const string CoderNotificationHandler = "CoderNotificationHandler";

    private readonly AppNotificationManager _notificationManager = AppNotificationManager.Default;

    private ConcurrentDictionary<string, INotificationHandler> Handlers { get; } = new();

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public void RegisterHandler(string name, INotificationHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));
        if (handler is IUserNotifier)
            throw new ArgumentException("Handler cannot be an IUserNotifier", nameof(handler));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or whitespace", nameof(name));
        if (!Handlers.TryAdd(name, handler))
            throw new InvalidOperationException($"A handler with the name '{name}' is already registered.");
    }

    public Task ShowErrorNotification(string title, string message, CancellationToken ct = default)
    {
        var builder = new AppNotificationBuilder().AddText(title).AddText(message);
        _notificationManager.Show(builder.BuildNotification());
        return Task.CompletedTask;
    }

    public Task ShowActionNotification(string title, string message, string handlerName, IDictionary<string, string>? args = null, CancellationToken ct = default)
    {
        if (!Handlers.TryGetValue(handlerName, out _))
        {
            logger.LogWarning("no action handler found for notification with name {HandlerName}, ignoring", handlerName);
            return Task.CompletedTask;
        }

        var builder = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .AddArgument(CoderNotificationHandler, handlerName);
        if (args != null)
            foreach (var arg in args)
            {
                if (arg.Key == CoderNotificationHandler)
                    continue;
                builder.AddArgument(arg.Key, arg.Value);
            }

        _notificationManager.Show(builder.BuildNotification());
        return Task.CompletedTask;
    }

    public void HandleNotificationActivation(IDictionary<string, string> args)
    {
        if (!args.TryGetValue(CoderNotificationHandler, out var handlerName))
            // Not an action notification, ignore
            return;

        if (!Handlers.TryGetValue(handlerName, out var handler))
        {
            logger.LogWarning("no action handler '{HandlerName}' found for notification activation, ignoring", handlerName);
            return;
        }

        dispatcherQueueManager.RunInUiThread(() =>
        {
            try
            {
                handler.HandleNotificationActivation(args);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "could not handle activation for notification with handler '{HandlerName}", handlerName);
            }
        });
    }
}

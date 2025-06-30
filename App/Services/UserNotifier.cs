using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Views;
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
    public void UnregisterHandler(string name);

    public Task ShowErrorNotification(string title, string message, CancellationToken ct = default);
    /// <summary>
    /// This method allows to display a Windows-native notification with an action defined in
    /// <paramref name="handlerName"/> and provided <paramref name="args"/>.
    /// </summary>
    /// <param name="title">Title of the notification.</param>
    /// <param name="message">Message to be displayed in the notification body.</param>
    /// <param name="handlerName">Handler should be e.g. <c>nameof(Handler)</c> where <c>Handler</c>
    /// implements <see cref="Coder.Desktop.App.Services.INotificationHandler" />.
    /// If handler is <c>null</c> the action will open Coder Desktop.</param>
    /// <param name="args">Arguments to be provided to the handler when executing the action.</param>
    public Task ShowActionNotification(string title, string message, string? handlerName, IDictionary<string, string>? args = null, CancellationToken ct = default);
}

public class UserNotifier : IUserNotifier
{
    private const string CoderNotificationHandler = "CoderNotificationHandler";
    private const string DefaultNotificationHandler = "DefaultNotificationHandler";

    private readonly AppNotificationManager _notificationManager = AppNotificationManager.Default;
    private readonly ILogger<UserNotifier> _logger;
    private readonly IDispatcherQueueManager _dispatcherQueueManager;

    private ConcurrentDictionary<string, INotificationHandler> Handlers { get; } = new();

    public UserNotifier(ILogger<UserNotifier> logger, IDispatcherQueueManager dispatcherQueueManager,
        INotificationHandler notificationHandler)
    {
        _logger = logger;
        _dispatcherQueueManager = dispatcherQueueManager;
        var defaultHandlerAdded = Handlers.TryAdd(DefaultNotificationHandler, notificationHandler);
        if (!defaultHandlerAdded)
            throw new Exception($"UserNotifier failed to be initialized with {nameof(DefaultNotificationHandler)}");
    }

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

    public void UnregisterHandler(string name)
    {
        if (name == nameof(DefaultNotificationHandler))
            throw new InvalidOperationException($"You cannot remove '{name}'.");
        if (!Handlers.TryRemove(name, out _))
            throw new InvalidOperationException($"No handler with the name '{name}' is registered.");
    }

    public Task ShowErrorNotification(string title, string message, CancellationToken ct = default)
    {
        var builder = new AppNotificationBuilder().AddText(title).AddText(message);
        _notificationManager.Show(builder.BuildNotification());
        return Task.CompletedTask;
    }

    public Task ShowActionNotification(string title, string message, string? handlerName, IDictionary<string, string>? args = null, CancellationToken ct = default)
    {
        if (handlerName == null)
            handlerName = nameof(DefaultNotificationHandler); // Use default handler if no handler name is provided

        if (!Handlers.TryGetValue(handlerName, out _))
            throw new InvalidOperationException($"No action handler with the name '{handlerName}' is registered.");

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
            _logger.LogWarning("no action handler '{HandlerName}' found for notification activation, ignoring", handlerName);
            return;
        }

        _dispatcherQueueManager.RunInUiThread(() =>
        {
            try
            {
                handler.HandleNotificationActivation(args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "could not handle activation for notification with handler '{HandlerName}", handlerName);
            }
        });
    }
}

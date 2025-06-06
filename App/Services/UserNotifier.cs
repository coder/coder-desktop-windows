using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Coder.Desktop.App.Services;

public interface IUserNotifier : IAsyncDisposable
{
    public Task ShowErrorNotification(string title, string message, CancellationToken ct = default);
    public Task ShowActionNotification(string title, string message, Action action, CancellationToken ct = default);

    public void HandleActivation(AppNotificationActivatedEventArgs args);
}

public class UserNotifier(ILogger<UserNotifier> logger) : IUserNotifier
{
    private const string CoderNotificationId = "CoderNotificationId";

    private readonly AppNotificationManager _notificationManager = AppNotificationManager.Default;

    public ConcurrentDictionary<string, Action> ActionHandlers { get; } = new();

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public Task ShowErrorNotification(string title, string message, CancellationToken ct = default)
    {
        var builder = new AppNotificationBuilder().AddText(title).AddText(message);
        _notificationManager.Show(builder.BuildNotification());
        return Task.CompletedTask;
    }

    public Task ShowActionNotification(string title, string message, Action action, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .AddArgument(CoderNotificationId, id)
            .BuildNotification();
        ActionHandlers[id] = action;
        _notificationManager.Show(notification);
        return Task.CompletedTask;
    }

    public void HandleActivation(AppNotificationActivatedEventArgs args)
    {
        // Must not be an Action notification.
        if (!args.Arguments.TryGetValue(CoderNotificationId, out var id))
            return;

        if (!ActionHandlers.TryRemove(id, out var action))
        {
            logger.LogWarning("no action handler found for notification with ID {NotificationId}, ignoring", id);
            return;
        }

        var dispatcherQueue = ((App)Application.Current).TrayWindow?.DispatcherQueue;
        if (dispatcherQueue == null)
        {
            logger.LogError("could not acquire DispatcherQueue for notification event handling, is TrayWindow active?");
            return;
        }
        if (!dispatcherQueue.HasThreadAccess)
        {
            dispatcherQueue.TryEnqueue(RunAction);
            return;
        }

        RunAction();

        return;

        void RunAction()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "could not handle activation for notification with ID {NotificationId}", id);
            }
        }
    }

}

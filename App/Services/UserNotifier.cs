using System;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Coder.Desktop.App.Services;

public interface IUserNotifier : IAsyncDisposable
{
    public Task ShowErrorNotification(string title, string message);
}

public class UserNotifier : IUserNotifier
{
    private readonly AppNotificationManager _notificationManager = AppNotificationManager.Default;

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public Task ShowErrorNotification(string title, string message)
    {
        var builder = new AppNotificationBuilder().AddText(title).AddText(message);
        _notificationManager.Show(builder.BuildNotification());
        return Task.CompletedTask;
    }
}


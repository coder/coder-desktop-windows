using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Coder.Desktop.App.Diagnostics;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IWindowService"/>.
/// </summary>
public sealed class AvaloniaWindowService(IServiceProvider services) : IWindowService
{
    private SignInWindow? _signInWindow;
    private SettingsWindow? _settingsWindow;
    private FileSyncListWindow? _fileSyncListWindow;

    public void ShowSignInWindow()
    {
        if (_signInWindow is { } existing)
        {
            if (!existing.IsVisible)
                existing.Show();
            existing.Activate();
            return;
        }

        var vm = services.GetRequiredService<SignInViewModel>();
        var window = new SignInWindow(vm);
        window.Closed += (_, _) => _signInWindow = null;
        _signInWindow = window;
        ShowWindow(window, useOwner: false);
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow is { } existing)
        {
            if (!existing.IsVisible)
                existing.Show();
            existing.Activate();
            return;
        }

        var vm = services.GetRequiredService<SettingsViewModel>();
        var window = new SettingsWindow
        {
            DataContext = vm,
        };
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        ShowWindow(window, useOwner: false);
    }

    public void ShowFileSyncListWindow()
    {
        if (_fileSyncListWindow is { } existing)
        {
            if (!existing.IsVisible)
                existing.Show();
            existing.Activate();
            return;
        }

        var vm = services.GetRequiredService<FileSyncListViewModel>();
        var window = new FileSyncListWindow(vm);
        window.Closed += (_, _) =>
        {
            vm.Dispose();
            _fileSyncListWindow = null;
        };

        _fileSyncListWindow = window;
        ShowWindow(window, useOwner: false);
    }

    public void ShowMessageWindow(string title, string message, string windowTitle)
    {
        var window = new MessageWindow(title, message, windowTitle);
        ShowWindow(window, useOwner: false);
    }

    private static void ShowWindow(Window window, bool useOwner)
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

        if (desktop?.MainWindow is TrayWindow trayWindow && trayWindow != window && trayWindow.IsVisible)
            trayWindow.Hide();

        var owner = useOwner ? desktop?.MainWindow : null;
        if (owner != null && owner != window && owner.IsVisible)
            window.Show(owner);
        else
            window.Show();

        window.Activate();
        AppBootstrapLogger.Info($"Opened window: {window.GetType().Name}");
    }
}

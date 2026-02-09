using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private TrayWindow? _trayWindow;
    private TrayIconViewModel? _trayIconViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Register cross-platform services from App.Shared
        services.AddSingleton<IDispatcher, AvaloniaDispatcher>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<ILauncherService, ProcessLauncherService>();

        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Create the tray popup window immediately so we can show/hide it
            // quickly from the tray icon.
            _trayWindow = new TrayWindow();

            // Make TrayWindow the MainWindow so dialogs can use it as an owner.
            desktop.MainWindow = _trayWindow;

            // Keep it hidden on startup; the user will open it from the tray.
            // (StartWithClassicDesktopLifetime will show MainWindow by default.)
            _trayWindow.RequestHideOnFirstOpen();

            _trayIconViewModel = new TrayIconViewModel(ToggleTrayWindow, () => desktop.Shutdown());
            ConfigureTrayIcons(_trayIconViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureTrayIcons(TrayIconViewModel trayIconViewModel)
    {
        // The tray icons are defined in App.axaml via the TrayIcon.Icons attached property.
        var icons = TrayIcon.GetIcons(this);
        if (icons is null)
            return;

        foreach (var trayIcon in icons)
        {
            // Ensure clicking the icon toggles the tray window.
            trayIcon.Clicked -= TrayIconOnClicked;
            trayIcon.Clicked += TrayIconOnClicked;

            // Also set the command explicitly so bindings don't depend on a DataContext
            // being available for tray icon objects.
            trayIcon.Command = trayIconViewModel.ShowWindowCommand;

            if (trayIcon.Menu is NativeMenu menu)
            {
                foreach (var item in menu.Items)
                {
                    if (item is not NativeMenuItem nativeItem)
                        continue;

                    switch (nativeItem.Header?.ToString())
                    {
                        case "Show":
                            nativeItem.Command = trayIconViewModel.ShowWindowCommand;
                            break;
                        case "Exit":
                            nativeItem.Command = trayIconViewModel.ExitCommand;
                            break;
                    }
                }
            }
        }
    }

    private void TrayIconOnClicked(object? sender, EventArgs e)
    {
        ToggleTrayWindow();
    }

    private void ToggleTrayWindow()
    {
        if (_trayWindow is null)
            return;

        if (_trayWindow.IsVisible)
        {
            _trayWindow.Hide();
            return;
        }

        _trayWindow.ShowNearSystemTray();
    }
}

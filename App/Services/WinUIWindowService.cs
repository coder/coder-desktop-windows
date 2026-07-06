using System;
using Coder.Desktop.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coder.Desktop.App.Services;

/// <summary>
///     WinUI implementation of IWindowService. Tracks singleton-style windows
///     so that showing an already-open window activates it instead of opening
///     a duplicate. All methods must be called from the UI thread.
/// </summary>
public class WinUIWindowService : IWindowService
{
    private readonly IServiceProvider _services;

    private SignInWindow? _signInWindow;
    private SettingsWindow? _settingsWindow;
    private FileSyncListWindow? _fileSyncListWindow;

    public WinUIWindowService(IServiceProvider services)
    {
        _services = services;
    }

    public void ShowSignInWindow()
    {
        if (_signInWindow != null)
        {
            _signInWindow.Activate();
            return;
        }

        _signInWindow = _services.GetRequiredService<SignInWindow>();
        _signInWindow.Closed += (_, _) => _signInWindow = null;
        _signInWindow.Activate();
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    public void ShowFileSyncListWindow()
    {
        if (_fileSyncListWindow != null)
        {
            _fileSyncListWindow.Activate();
            return;
        }

        _fileSyncListWindow = _services.GetRequiredService<FileSyncListWindow>();
        _fileSyncListWindow.Closed += (_, _) => _fileSyncListWindow = null;
        _fileSyncListWindow.Activate();
    }

    public void ShowMessageWindow(string title, string message, string windowTitle)
    {
        // MessageWindow shows itself when constructed.
        _ = new MessageWindow(title, message, windowTitle);
    }
}

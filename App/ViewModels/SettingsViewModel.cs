using System;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private Window? _window;
    private DispatcherQueue? _dispatcherQueue;

    private ISettingsManager _settingsManager;

    public SettingsViewModel(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public void Initialize(Window window, DispatcherQueue dispatcherQueue)
    {
        _window = window;
        _dispatcherQueue = dispatcherQueue;
        if (!_dispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Initialize must be called from the UI thread");
    }

    [RelayCommand]
    private void SaveSetting()
    {
        //_settingsManager.Save();
    }

    [RelayCommand]
    private void ShowSettingsDialog()
    {
        if (_window is null || _dispatcherQueue is null)
            throw new InvalidOperationException("Initialize must be called before showing the settings dialog.");
        // Here you would typically open a settings dialog or page.
        // For example, you could navigate to a SettingsPage in your app.
        // This is just a placeholder for demonstration purposes.
        // Display MessageBox and show a message
        var message = $"Settings dialog opened. Current setting: {_settingsManager.Read("SomeSetting", false)}\n" +
                      "You can implement your settings dialog here.";
        var dialog = new ContentDialog();
        dialog.Title = "Settings";
        dialog.Content = message;
        dialog.XamlRoot = _window.Content.XamlRoot;
        _ = dialog.ShowAsync();
    }
}

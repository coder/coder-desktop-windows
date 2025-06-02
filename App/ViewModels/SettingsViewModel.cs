using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Coder.Desktop.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private Window? _window;
    private DispatcherQueue? _dispatcherQueue;

    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    public partial bool ConnectOnLaunch { get; set; } = false;

    [ObservableProperty]
    public partial bool StartOnLogin { get; set; } = false;

    private ISettingsManager _settingsManager;

    public SettingsViewModel(ILogger<SettingsViewModel> logger, ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _logger = logger;
        ConnectOnLaunch = _settingsManager.Read(SettingsManager.ConnectOnLaunchKey, false);
        StartOnLogin = _settingsManager.Read(SettingsManager.StartOnLoginKey, false);

        this.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ConnectOnLaunch))
            {
                try
                {
                    _settingsManager.Save(SettingsManager.ConnectOnLaunchKey, ConnectOnLaunch);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving {SettingsManager.ConnectOnLaunchKey} setting: {ex.Message}");
                }
            }
            else if (args.PropertyName == nameof(StartOnLogin))
            {
                try
                {
                    _settingsManager.Save(SettingsManager.StartOnLoginKey, StartOnLogin);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving {SettingsManager.StartOnLoginKey} setting: {ex.Message}");
                }
            }
        };
    }

    public void Initialize(Window window, DispatcherQueue dispatcherQueue)
    {
        _window = window;
        _dispatcherQueue = dispatcherQueue;
        if (!_dispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Initialize must be called from the UI thread");
    }
}

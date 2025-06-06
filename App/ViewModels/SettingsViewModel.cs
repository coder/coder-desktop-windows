using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;

namespace Coder.Desktop.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    public partial bool ConnectOnLaunch { get; set; }

    [ObservableProperty]
    public partial bool StartOnLoginDisabled { get; set; }

    [ObservableProperty]
    public partial bool StartOnLogin { get; set; }

    private ISettingsManager<CoderConnectSettings> _connectSettingsManager;
    private CoderConnectSettings _connectSettings = new CoderConnectSettings();
    private IStartupManager _startupManager;

    public SettingsViewModel(ILogger<SettingsViewModel> logger, ISettingsManager<CoderConnectSettings> settingsManager, IStartupManager startupManager)
    {
        _connectSettingsManager = settingsManager;
        _startupManager = startupManager;
        _logger = logger;
        // Application settings are loaded on application startup,
        // so we expect the settings to be available immediately.
        var settingsCache = settingsManager.Read();
        StartOnLogin = startupManager.IsEnabled();
        ConnectOnLaunch = _connectSettings.ConnectOnLaunch;

        // Various policies can disable the "Start on login" option.
        // We disable the option in the UI if the policy is set.
        StartOnLoginDisabled = _startupManager.IsDisabledByPolicy();

        // Ensure the StartOnLogin property matches the current startup state.
        if (StartOnLogin != _startupManager.IsEnabled())
        {
            StartOnLogin = _startupManager.IsEnabled();
        }
    }

    partial void OnConnectOnLaunchChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue)
            return;
        try
        {
            _connectSettings.ConnectOnLaunch = ConnectOnLaunch;
            _connectSettingsManager.Write(_connectSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving Coder Connect settings: {ex.Message}");
        }
    }

    partial void OnStartOnLoginChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue)
            return;
        try
        {
            if (StartOnLogin)
            {
                _startupManager.Enable();
            }
            else
            {
                _startupManager.Disable();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error setting StartOnLogin in registry: {ex.Message}");
        }
    }
}

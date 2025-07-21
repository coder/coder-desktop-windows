using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;

namespace Coder.Desktop.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    public partial bool ConnectOnLaunch { get; set; }

    [ObservableProperty]
    public partial bool DisableTailscaleLoopProtection { get; set; }

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
        _connectSettings = settingsManager.Read().GetAwaiter().GetResult();
        StartOnLogin = startupManager.IsEnabled();
        ConnectOnLaunch = _connectSettings.ConnectOnLaunch;
        DisableTailscaleLoopProtection = _connectSettings.DisableTailscaleLoopProtection;

        // Various policies can disable the "Start on login" option.
        // We disable the option in the UI if the policy is set.
        StartOnLoginDisabled = _startupManager.IsDisabledByPolicy();

        // Ensure the StartOnLogin property matches the current startup state.
        if (StartOnLogin != _startupManager.IsEnabled())
        {
            StartOnLogin = _startupManager.IsEnabled();
        }
    }

    partial void OnDisableTailscaleLoopProtectionChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue)
            return;
        try
        {
            _connectSettings.DisableTailscaleLoopProtection = DisableTailscaleLoopProtection;
            _connectSettingsManager.Write(_connectSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving Coder Connect {nameof(DisableTailscaleLoopProtection)} settings: {ex.Message}");
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
            _logger.LogError($"Error saving Coder Connect {nameof(ConnectOnLaunch)} settings: {ex.Message}");
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

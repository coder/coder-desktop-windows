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

    private ISettingsManager _settingsManager;
    private IStartupManager _startupManager;

    public SettingsViewModel(ILogger<SettingsViewModel> logger, ISettingsManager settingsManager, IStartupManager startupManager)
    {
        _settingsManager = settingsManager;
        _startupManager = startupManager;
        _logger = logger;
        ConnectOnLaunch = _settingsManager.ConnectOnLaunch;
        StartOnLogin = _settingsManager.StartOnLogin;

        // Various policies can disable the "Start on login" option.
        // We disable the option in the UI if the policy is set.
        StartOnLoginDisabled = _startupManager.IsDisabledByPolicy();

        this.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ConnectOnLaunch))
            {
                try
                {
                    _settingsManager.ConnectOnLaunch = ConnectOnLaunch;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error saving {SettingsManager.ConnectOnLaunchKey} setting: {ex.Message}");
                }
            }
            else if (args.PropertyName == nameof(StartOnLogin))
            {
                try
                {
                    _settingsManager.StartOnLogin = StartOnLogin;
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
                    _logger.LogError($"Error saving {SettingsManager.StartOnLoginKey} setting: {ex.Message}");
                }
            }
        };

        // Ensure the StartOnLogin property matches the current startup state.
        if (StartOnLogin != _startupManager.IsEnabled())
        {
            StartOnLogin = _startupManager.IsEnabled();
        }
    }
}

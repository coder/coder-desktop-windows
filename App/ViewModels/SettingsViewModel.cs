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
    public partial bool ConnectOnLaunch { get; set; } = false;

    [ObservableProperty]
    public partial bool StartOnLoginDisabled { get; set; } = false;

    [ObservableProperty]
    public partial bool StartOnLogin { get; set; } = false;

    private ISettingsManager _settingsManager;

    public SettingsViewModel(ILogger<SettingsViewModel> logger, ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _logger = logger;
        ConnectOnLaunch = _settingsManager.Read(SettingsManager.ConnectOnLaunchKey, false);
        StartOnLogin = _settingsManager.Read(SettingsManager.StartOnLoginKey, false);

        // Various policies can disable the "Start on login" option.
        // We disable the option in the UI if the policy is set.
        StartOnLoginDisabled = StartupManager.IsDisabledByPolicy();

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
                    _logger.LogError($"Error saving {SettingsManager.ConnectOnLaunchKey} setting: {ex.Message}");
                }
            }
            else if (args.PropertyName == nameof(StartOnLogin))
            {
                try
                {
                    _settingsManager.Save(SettingsManager.StartOnLoginKey, StartOnLogin);
                    if (StartOnLogin)
                    {
                        StartupManager.Enable();
                    }
                    else
                    {
                        StartupManager.Disable();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error saving {SettingsManager.StartOnLoginKey} setting: {ex.Message}");
                }
            }
        };

        // Ensure the StartOnLogin property matches the current startup state.
        if (StartOnLogin != StartupManager.IsEnabled())
        {
            StartOnLogin = StartupManager.IsEnabled();
        }
    }
}

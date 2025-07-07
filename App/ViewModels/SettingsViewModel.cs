using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Views.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static Coder.Desktop.App.Models.CoderConnectSettings;

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
    private IRpcController _rpcController;

    private Window? _window;

    public IEnumerable<PortForward> PortForwards => _connectSettings.PortForwards;

    public SettingsViewModel(ILogger<SettingsViewModel> logger, ISettingsManager<CoderConnectSettings> settingsManager, IStartupManager startupManager,
        IRpcController rpcController)
    {
        _connectSettingsManager = settingsManager;
        _startupManager = startupManager;
        _logger = logger;
        _rpcController = rpcController;
        _connectSettings = settingsManager.Read().GetAwaiter().GetResult();
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

    public void Initialize(Window window)
    {
        _window = window;
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

    [RelayCommand]
    public async void AddPortForward()
    {
        var rpcModel = _rpcController.GetState();
        if(rpcModel.RpcLifecycle != RpcLifecycle.Connected)
        {
            _logger.LogWarning("Cannot add port forward, RPC is not connected.");
            return;
        }
        var hosts = new List<string>(rpcModel.Agents.Count);
        // Agents will only contain started agents.
        foreach (var agent in rpcModel.Agents)
        {
            var fqdn = agent.Fqdn
                .Select(a => a.Trim('.'))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Aggregate((a, b) => a.Count(c => c == '.') < b.Count(c => c == '.') ? a : b);
            if (string.IsNullOrWhiteSpace(fqdn))
                continue;
            hosts.Add(fqdn);
        }
        var dialog = new ContentDialog();

        dialog.XamlRoot = _window?.Content.XamlRoot ?? throw new InvalidOperationException("Window content XamlRoot is null.");
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.Title = "Save your work?";
        dialog.PrimaryButtonText = "Save";
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = new PortForwardCreation(hosts);

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var portForwardCreation = dialog.Content as PortForwardCreation;
            if (portForwardCreation != null)
            {
                _connectSettings.PortForwards.Add(portForwardCreation.PortForward);
                _connectSettingsManager.Write(_connectSettings).GetAwaiter().GetResult();
            }            
        }
    }
}

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowViewModel : ObservableObject
{
    private const int MaxAgents = 5;

    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;

    [ObservableProperty]
    public partial VpnLifecycle VpnLifecycle { get; set; } =
        VpnLifecycle.Stopping; // to prevent interaction until we get the real state

    // VpnSwitchOn needs to be its own property as it is a two-way binding
    [ObservableProperty]
    public partial bool VpnSwitchOn { get; set; } = false;

    [ObservableProperty]
    public partial string? VpnFailedMessage { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoAgents))]
    [NotifyPropertyChangedFor(nameof(AgentOverflow))]
    [NotifyPropertyChangedFor(nameof(VisibleAgents))]
    public partial ObservableCollection<AgentViewModel> Agents { get; set; } = [];

    public bool NoAgents => Agents.Count == 0;

    public bool AgentOverflow => Agents.Count > MaxAgents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleAgents))]
    public partial bool ShowAllAgents { get; set; } = false;

    public IEnumerable<AgentViewModel> VisibleAgents => ShowAllAgents ? Agents : Agents.Take(MaxAgents);

    [ObservableProperty]
    public partial string DashboardUrl { get; set; } = "https://coder.com";

    public TrayWindowViewModel(IRpcController rpcController, ICredentialManager credentialManager)
    {
        _rpcController = rpcController;
        _credentialManager = credentialManager;

        _rpcController.StateChanged += (_, rpcModel) => UpdateFromRpcModel(rpcModel);
        UpdateFromRpcModel(_rpcController.GetState());

        _credentialManager.CredentialsChanged += (_, credentialModel) => UpdateFromCredentialsModel(credentialModel);
        UpdateFromCredentialsModel(_credentialManager.GetCredentials());
    }

    private void UpdateFromRpcModel(RpcModel rpcModel)
    {
        // As a failsafe, if RPC is disconnected we disable the switch. The
        // Window should not show the current Page if the RPC is disconnected.
        if (rpcModel.RpcLifecycle is RpcLifecycle.Disconnected)
        {
            VpnLifecycle = VpnLifecycle.Stopping;
            VpnSwitchOn = false;
            Agents = [];
            return;
        }

        VpnLifecycle = rpcModel.VpnLifecycle;
        VpnSwitchOn = rpcModel.VpnLifecycle is VpnLifecycle.Starting or VpnLifecycle.Started;
        // TODO: convert from RpcModel once we send agent data
        Agents =
        [
            new AgentViewModel
            {
                Hostname = "pog",
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Green,
                DashboardUrl = "https://dev.coder.com/@dean/pog",
            },
            new AgentViewModel
            {
                Hostname = "pog2",
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Gray,
                DashboardUrl = "https://dev.coder.com/@dean/pog2",
            },
            new AgentViewModel
            {
                Hostname = "pog3",
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Red,
                DashboardUrl = "https://dev.coder.com/@dean/pog3",
            },
            new AgentViewModel
            {
                Hostname = "pog4",
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Red,
                DashboardUrl = "https://dev.coder.com/@dean/pog4",
            },
            new AgentViewModel
            {
                Hostname = "pog5",
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Red,
                DashboardUrl = "https://dev.coder.com/@dean/pog5",
            },
            new AgentViewModel
            {
                Hostname = "pog6",
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Red,
                DashboardUrl = "https://dev.coder.com/@dean/pog6",
            },
        ];

        if (Agents.Count < MaxAgents) ShowAllAgents = false;
    }

    private void UpdateFromCredentialsModel(CredentialModel credentialModel)
    {
        // HACK: the HyperlinkButton crashes the whole app if the initial URI
        // or this URI is invalid. CredentialModel.CoderUrl should never be
        // null while the Page is active as the Page is only displayed when
        // CredentialModel.Status == Valid.
        DashboardUrl = credentialModel.CoderUrl ?? "https://coder.com";
    }

    // VpnSwitch_Toggled is handled separately than just listening to the
    // property change as we need to be able to tell the difference between the
    // user toggling the switch and the switch being toggled by code.
    public void VpnSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch) return;

        VpnFailedMessage = "";
        try
        {
            if (toggleSwitch.IsOn)
                _rpcController.StartVpn();
            else
                _rpcController.StopVpn();
        }
        catch
        {
            VpnFailedMessage = e.ToString();
        }
    }

    [RelayCommand]
    public void ToggleShowAllAgents()
    {
        ShowAllAgents = !ShowAllAgents;
    }

    [RelayCommand]
    public void SignOut()
    {
        // TODO: this should either be blocked until the VPN is stopped or it should stop the VPN
        _credentialManager.ClearCredentials();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowViewModel : ObservableObject
{
    private const int MaxAgents = 5;

    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;

    private DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    public partial VpnLifecycle VpnLifecycle { get; set; } = VpnLifecycle.Unknown;

    // VpnSwitchOn needs to be its own property as it is a two-way binding
    [ObservableProperty]
    public partial bool VpnSwitchOn { get; set; } = false;

    [ObservableProperty]
    public partial string? VpnFailedMessage { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoAgents))]
    [NotifyPropertyChangedFor(nameof(AgentOverflow))]
    [NotifyPropertyChangedFor(nameof(VisibleAgents))]
    public partial List<AgentViewModel> Agents { get; set; } = [];

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
    }

    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        _rpcController.StateChanged += (_, rpcModel) => UpdateFromRpcModel(rpcModel);
        UpdateFromRpcModel(_rpcController.GetState());

        _credentialManager.CredentialsChanged += (_, credentialModel) => UpdateFromCredentialsModel(credentialModel);
        UpdateFromCredentialsModel(_credentialManager.GetCredentials());
    }

    private void UpdateFromRpcModel(RpcModel rpcModel)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateFromRpcModel(rpcModel));
            return;
        }

        // As a failsafe, if RPC is disconnected we disable the switch. The
        // Window should not show the current Page if the RPC is disconnected.
        if (rpcModel.RpcLifecycle is RpcLifecycle.Disconnected)
        {
            VpnLifecycle = VpnLifecycle.Unknown;
            VpnSwitchOn = false;
            Agents = [];
            return;
        }

        VpnLifecycle = rpcModel.VpnLifecycle;
        VpnSwitchOn = rpcModel.VpnLifecycle is VpnLifecycle.Starting or VpnLifecycle.Started;

        // Add every known agent.
        HashSet<ByteString> workspacesWithAgents = [];
        List<AgentViewModel> agents = [];
        foreach (var agent in rpcModel.Agents)
        {
            // Find the FQDN with the least amount of dots and split it into
            // prefix and suffix.
            var fqdn = agent.Fqdn
                .Select(a => a.Trim('.'))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Aggregate((a, b) => a.Count(c => c == '.') < b.Count(c => c == '.') ? a : b);
            if (string.IsNullOrWhiteSpace(fqdn))
                continue;

            var fqdnPrefix = fqdn;
            var fqdnSuffix = "";
            if (fqdn.Contains('.'))
            {
                fqdnPrefix = fqdn[..fqdn.LastIndexOf('.')];
                fqdnSuffix = fqdn[fqdn.LastIndexOf('.')..];
            }

            var lastHandshakeAgo = DateTime.UtcNow.Subtract(agent.LastHandshake.ToDateTime());
            workspacesWithAgents.Add(agent.WorkspaceId);
            agents.Add(new AgentViewModel
            {
                Hostname = fqdnPrefix,
                HostnameSuffix = fqdnSuffix,
                ConnectionStatus = lastHandshakeAgo < TimeSpan.FromMinutes(5)
                    ? AgentConnectionStatus.Green
                    : AgentConnectionStatus.Red,
                // TODO: we don't actually have any way of crafting a dashboard
                // URL without the owner's username
                DashboardUrl = "https://coder.com",
            });
        }

        // For every workspace that doesn't have an agent, add a dummy agent.
        foreach (var workspace in rpcModel.Workspaces.Where(w => !workspacesWithAgents.Contains(w.Id)))
        {
            agents.Add(new AgentViewModel
            {
                // We just assume that it's a single-agent workspace.
                Hostname = workspace.Name,
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Gray,
                // TODO: we don't actually have any way of crafting a dashboard
                // URL without the owner's username
                DashboardUrl = "https://coder.com",
            });
        }

        // Sort by status green, red, gray, then by hostname.
        agents.Sort((a, b) =>
        {
            if (a.ConnectionStatus != b.ConnectionStatus)
                return a.ConnectionStatus.CompareTo(b.ConnectionStatus);
            return string.Compare(a.FullHostname, b.FullHostname, StringComparison.Ordinal);
        });
        Agents = agents;

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
        if (VpnLifecycle is not VpnLifecycle.Stopped)
            return;
        _credentialManager.ClearCredentials();
    }
}

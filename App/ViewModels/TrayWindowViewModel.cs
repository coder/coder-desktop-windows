using System;
using System.Collections.Generic;
using System.Linq;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.Vpn.Proto;
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
    private const string DefaultDashboardUrl = "https://coder.com";

    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;

    private DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    public partial VpnLifecycle VpnLifecycle { get; set; } = VpnLifecycle.Unknown;

    // This is a separate property because we need the switch to be 2-way.
    [ObservableProperty]
    public partial bool VpnSwitchActive { get; set; } = false;

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
            VpnSwitchActive = false;
            Agents = [];
            return;
        }

        VpnLifecycle = rpcModel.VpnLifecycle;
        VpnSwitchActive = rpcModel.VpnLifecycle is VpnLifecycle.Starting or VpnLifecycle.Started;

        // Get the current dashboard URL.
        var credentialModel = _credentialManager.GetCredentials();
        Uri? coderUri = null;
        if (credentialModel.State == CredentialState.Valid && !string.IsNullOrWhiteSpace(credentialModel.CoderUrl))
            try
            {
                coderUri = new Uri(credentialModel.CoderUrl, UriKind.Absolute);
            }
            catch
            {
                // Ignore
            }

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
            var workspace = rpcModel.Workspaces.FirstOrDefault(w => w.Id == agent.WorkspaceId);

            agents.Add(new AgentViewModel
            {
                Hostname = fqdnPrefix,
                HostnameSuffix = fqdnSuffix,
                ConnectionStatus = lastHandshakeAgo < TimeSpan.FromMinutes(5)
                    ? AgentConnectionStatus.Green
                    : AgentConnectionStatus.Red,
                DashboardUrl = WorkspaceUri(coderUri, workspace?.Name),
            });
        }

        // For every stopped workspace that doesn't have any agents, add a
        // dummy agent row.
        foreach (var workspace in rpcModel.Workspaces.Where(w => w.Status == Workspace.Types.Status.Stopped && !workspacesWithAgents.Contains(w.Id)))
            agents.Add(new AgentViewModel
            {
                // We just assume that it's a single-agent workspace.
                Hostname = workspace.Name,
                HostnameSuffix = ".coder",
                ConnectionStatus = AgentConnectionStatus.Gray,
                DashboardUrl = WorkspaceUri(coderUri, workspace.Name),
            });

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

    private string WorkspaceUri(Uri? baseUri, string? workspaceName)
    {
        if (baseUri == null) return DefaultDashboardUrl;
        if (string.IsNullOrWhiteSpace(workspaceName)) return baseUri.ToString();
        try
        {
            return new Uri(baseUri, $"/@me/{workspaceName}").ToString();
        }
        catch
        {
            return DefaultDashboardUrl;
        }
    }

    private void UpdateFromCredentialsModel(CredentialModel credentialModel)
    {
        // HACK: the HyperlinkButton crashes the whole app if the initial URI
        // or this URI is invalid. CredentialModel.CoderUrl should never be
        // null while the Page is active as the Page is only displayed when
        // CredentialModel.Status == Valid.
        DashboardUrl = credentialModel.CoderUrl ?? DefaultDashboardUrl;
    }

    public void VpnSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch) return;

        VpnFailedMessage = "";
        try
        {
            // The start/stop methods will call back to update the state.
            if (toggleSwitch.IsOn && VpnLifecycle is VpnLifecycle.Stopped)
                _rpcController.StartVpn();
            else if (!toggleSwitch.IsOn && VpnLifecycle is VpnLifecycle.Started)
                _rpcController.StopVpn();
            else
                toggleSwitch.IsOn = VpnLifecycle is VpnLifecycle.Starting or VpnLifecycle.Started;
        }
        catch
        {
            // TODO: display error
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

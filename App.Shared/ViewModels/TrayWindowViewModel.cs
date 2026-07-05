using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.Vpn.Proto;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentExpanderHost
{
    public void HandleAgentExpanded(Uuid id, bool expanded);
}

public partial class TrayWindowViewModel : ObservableObject, IAgentExpanderHost
{
    private const int MaxAgents = 5;
    private const string DefaultDashboardUrl = "https://coder.com";
    private readonly TimeSpan HealthyPingThreshold = TimeSpan.FromMilliseconds(150);

    private readonly ILogger<TrayWindowViewModel> _logger;
    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;
    private readonly IAgentViewModelFactory _agentViewModelFactory;
    private readonly IHostnameSuffixGetter _hostnameSuffixGetter;
    private readonly IDispatcher _dispatcher;
    private readonly IWindowService _windowService;
    private readonly IHostApplicationLifetime _applicationLifetime;

    private bool _settingVpnSwitchState;

    // When we transition from 0 online workspaces to >0 online workspaces, the
    // first agent will be expanded. This bool tracks whether this has occurred
    // yet (or if the user has expanded something themselves).
    private bool _hasExpandedAgent;

    private readonly object _pendingRpcModelLock = new();
    private RpcModel? _pendingRpcModel;
    private bool _rpcUpdateQueued;

    // This isn't an ObservableProperty because the property itself never
    // changes. We add an event listener for the collection changing in the
    // constructor.
    public readonly ObservableCollection<AgentViewModel> Agents = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnableSection))]
    [NotifyPropertyChangedFor(nameof(ShowVpnStartProgressSection))]
    [NotifyPropertyChangedFor(nameof(ShowWorkspacesHeader))]
    [NotifyPropertyChangedFor(nameof(ShowNoAgentsSection))]
    [NotifyPropertyChangedFor(nameof(ShowAgentsSection))]
    private VpnLifecycle _vpnLifecycle = VpnLifecycle.Unknown;

    // This is a separate property because we need the switch to be 2-way.
    [ObservableProperty]
    private bool _vpnSwitchActive = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnableSection))]
    [NotifyPropertyChangedFor(nameof(ShowVpnStartProgressSection))]
    [NotifyPropertyChangedFor(nameof(ShowWorkspacesHeader))]
    [NotifyPropertyChangedFor(nameof(ShowNoAgentsSection))]
    [NotifyPropertyChangedFor(nameof(ShowAgentsSection))]
    [NotifyPropertyChangedFor(nameof(ShowAgentOverflowButton))]
    [NotifyPropertyChangedFor(nameof(ShowFailedSection))]
    private string? _vpnFailedMessage = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VpnStartProgressIsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(VpnStartProgressValueOrDefault))]
    private int? _vpnStartProgressValue = null;

    public int VpnStartProgressValueOrDefault => VpnStartProgressValue ?? 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VpnStartProgressMessageOrDefault))]
    private string? _vpnStartProgressMessage = null;

    public string VpnStartProgressMessageOrDefault =>
        string.IsNullOrEmpty(VpnStartProgressMessage) ? VpnStartupProgress.DefaultStartProgressMessage : VpnStartProgressMessage;

    public bool VpnStartProgressIsIndeterminate => VpnStartProgressValueOrDefault == 0;

    public bool ShowEnableSection => VpnFailedMessage is null && VpnLifecycle is not VpnLifecycle.Starting and not VpnLifecycle.Started;

    public bool ShowVpnStartProgressSection => VpnFailedMessage is null && VpnLifecycle is VpnLifecycle.Starting;

    public bool ShowWorkspacesHeader => VpnFailedMessage is null && VpnLifecycle is VpnLifecycle.Started;

    public bool ShowNoAgentsSection =>
        VpnFailedMessage is null && Agents.Count == 0 && VpnLifecycle is VpnLifecycle.Started;

    public bool ShowAgentsSection =>
        VpnFailedMessage is null && Agents.Count > 0 && VpnLifecycle is VpnLifecycle.Started;

    public bool ShowFailedSection => VpnFailedMessage is not null;

    public bool ShowAgentOverflowButton => VpnFailedMessage is null && Agents.Count > MaxAgents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleAgents))]
    private bool _showAllAgents = false;

    public IReadOnlyList<AgentViewModel> VisibleAgents =>
        (ShowAllAgents ? Agents : Agents.Take(MaxAgents)).ToList();

    [ObservableProperty]
    private string _dashboardUrl = DefaultDashboardUrl;

    public TrayWindowViewModel(
        ILogger<TrayWindowViewModel> logger,
        IRpcController rpcController,
        ICredentialManager credentialManager,
        IAgentViewModelFactory agentViewModelFactory,
        IHostnameSuffixGetter hostnameSuffixGetter,
        IDispatcher dispatcher,
        IWindowService windowService,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _rpcController = rpcController;
        _credentialManager = credentialManager;
        _agentViewModelFactory = agentViewModelFactory;
        _hostnameSuffixGetter = hostnameSuffixGetter;
        _dispatcher = dispatcher;
        _windowService = windowService;
        _applicationLifetime = applicationLifetime;

        _rpcController.StateChanged += (_, rpcModel) => QueueRpcModelUpdate(rpcModel);
        _credentialManager.CredentialsChanged += (_, credentialModel) => UpdateFromCredentialModel(credentialModel);
        _hostnameSuffixGetter.SuffixChanged += (_, suffix) => HandleHostnameSuffixChanged(suffix);

        UpdateFromRpcModel(_rpcController.GetState());
        UpdateFromCredentialModel(_credentialManager.GetCachedCredentials());
        HandleHostnameSuffixChanged(_hostnameSuffixGetter.GetCachedSuffix());
    }

    // Implements IAgentExpanderHost
    public void HandleAgentExpanded(Uuid id, bool expanded)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => HandleAgentExpanded(id, expanded));
            return;
        }

        _logger.LogInformation("HandleAgentExpanded id={AgentId} expanded={Expanded}", id, expanded);
        if (!expanded) return;

        _hasExpandedAgent = true;
        // Collapse every other agent.
        foreach (var otherAgent in Agents.Where(a => a.Id != id && a.IsExpanded == true))
            otherAgent.SetExpanded(false);
    }

    private void NotifyAgentCollectionStateChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(VisibleAgents)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowNoAgentsSection)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowAgentsSection)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowAgentOverflowButton)));
    }

    private void QueueRpcModelUpdate(RpcModel rpcModel)
    {
        var shouldQueue = false;
        lock (_pendingRpcModelLock)
        {
            _pendingRpcModel = rpcModel;
            if (!_rpcUpdateQueued)
            {
                _rpcUpdateQueued = true;
                shouldQueue = true;
            }
        }

        if (!shouldQueue)
            return;

        _dispatcher.Post(() =>
        {
            RpcModel? latest;
            lock (_pendingRpcModelLock)
            {
                latest = _pendingRpcModel;
                _pendingRpcModel = null;
                _rpcUpdateQueued = false;
            }

            if (latest is not null)
                UpdateFromRpcModel(latest);
        });
    }

    private void UpdateFromRpcModel(RpcModel rpcModel)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _logger.LogInformation("UpdateFromRpcModel coalescing to UI thread (vpn={Lifecycle}, agents={Count}, workspaces={WsCount})",
                rpcModel.VpnLifecycle, rpcModel.Agents.Count, rpcModel.Workspaces.Count);
            QueueRpcModelUpdate(rpcModel);
            return;
        }

        _logger.LogInformation("UpdateFromRpcModel running on UI thread (vpn={Lifecycle}, agents={Count}, workspaces={WsCount})",
            rpcModel.VpnLifecycle, rpcModel.Agents.Count, rpcModel.Workspaces.Count);

        // As a failsafe, if RPC is disconnected (or we're not signed in) we
        // disable the switch. The Window should not show the current Page if
        // the RPC is disconnected.
        var credentialModel = _credentialManager.GetCachedCredentials();
        if (rpcModel.RpcLifecycle is RpcLifecycle.Disconnected || credentialModel.State is not CredentialState.Valid ||
            credentialModel.CoderUrl == null)
        {
            VpnLifecycle = VpnLifecycle.Unknown;
            SetVpnSwitchFromLifecycle();

            if (Agents.Count > 0)
            {
                Agents.Clear();
                NotifyAgentCollectionStateChanged();
            }

            return;
        }

        if (VpnLifecycle != rpcModel.VpnLifecycle)
            _logger.LogInformation("VPN lifecycle: {Old} -> {New}", VpnLifecycle, rpcModel.VpnLifecycle);

        VpnLifecycle = rpcModel.VpnLifecycle;
        SetVpnSwitchFromLifecycle();

        // VpnStartupProgress is only set when the VPN is starting.
        if (rpcModel.VpnLifecycle is VpnLifecycle.Starting && rpcModel.VpnStartupProgress != null)
        {
            // Convert 0.00-1.00 to 0-100.
            var progress = (int)(rpcModel.VpnStartupProgress.Progress * 100);
            VpnStartProgressValue = Math.Clamp(progress, 0, 100);
            VpnStartProgressMessage = rpcModel.VpnStartupProgress.ToString();
        }
        else
        {
            VpnStartProgressValue = null;
            VpnStartProgressMessage = null;
        }

        // Add every known agent.
        HashSet<ByteString> workspacesWithAgents = [];
        List<AgentViewModel> agents = [];
        foreach (var agent in rpcModel.Agents)
        {
            if (!Uuid.TryFrom(agent.Id.Span, out var uuid))
                continue;

            // Find the FQDN with the least amount of dots and split it into
            // prefix and suffix. Some agents may not have any FQDN values yet,
            // so handle empty sets without throwing.
            var fqdn = agent.Fqdn
                .Select(a => a.Trim('.'))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .OrderBy(a => a.Count(c => c == '.'))
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fqdn))
                continue;

            var connectionStatus = AgentConnectionStatus.Healthy;

            if (agent.LastHandshake != null && agent.LastHandshake.ToDateTime() != default && agent.LastHandshake.ToDateTime() < DateTime.UtcNow)
            {
                // For compatibility with older deployments, we assume that if the
                // last ping is null, the agent is healthy.
                var isLatencyAcceptable = agent.LastPing == null || agent.LastPing.Latency.ToTimeSpan() < HealthyPingThreshold;

                var lastHandshakeAgo = DateTime.UtcNow.Subtract(agent.LastHandshake.ToDateTime());

                if (lastHandshakeAgo > TimeSpan.FromMinutes(5))
                    connectionStatus = AgentConnectionStatus.NoRecentHandshake;
                else if (!isLatencyAcceptable)
                    connectionStatus = AgentConnectionStatus.Unhealthy;
            }
            else
            {
                // If the last handshake is not correct (null, default or in the future),
                // we assume the agent is connecting (yellow status icon).
                connectionStatus = AgentConnectionStatus.Connecting;
            }

            workspacesWithAgents.Add(agent.WorkspaceId);
            var workspace = rpcModel.Workspaces.FirstOrDefault(w => w.Id == agent.WorkspaceId);

            agents.Add(_agentViewModelFactory.Create(
                this,
                uuid,
                fqdn,
                _hostnameSuffixGetter.GetCachedSuffix(),
                connectionStatus,
                credentialModel.CoderUrl,
                workspace?.Name,
                agent.LastPing?.DidP2P,
                agent.LastPing?.PreferredDerp,
                agent.LastPing?.Latency?.ToTimeSpan(),
                agent.LastPing?.PreferredDerpLatency?.ToTimeSpan(),
                agent.LastHandshake != null && agent.LastHandshake.ToDateTime() != default ? agent.LastHandshake?.ToDateTime() : null));
        }

        // For every stopped workspace that doesn't have any agents, add a
        // dummy agent row.
        foreach (var workspace in rpcModel.Workspaces.Where(w =>
                     ShouldShowDummy(w) && !workspacesWithAgents.Contains(w.Id)))
        {
            if (!Uuid.TryFrom(workspace.Id.Span, out var uuid))
                continue;

            agents.Add(_agentViewModelFactory.CreateDummy(
                this,
                // Workspace ID is fine as a stand-in here, it shouldn't
                // conflict with any agent IDs.
                uuid,
                _hostnameSuffixGetter.GetCachedSuffix(),
                AgentConnectionStatus.Offline,
                credentialModel.CoderUrl,
                workspace.Name));
        }

        if (rpcModel.Agents.Count > 0 || agents.Count > 0)
            _logger.LogInformation(
                "Agent update: {RpcAgentCount} proto agents -> {ViewModelCount} view models, {WsCount} workspaces",
                rpcModel.Agents.Count, agents.Count, rpcModel.Workspaces.Count);

        _logger.LogInformation("Applying agent list update (existing={ExistingCount}, incoming={IncomingCount})",
            Agents.Count, agents.Count);

        // Sort by status green, red, gray, then by hostname.
        ModelUpdate.ApplyLists(Agents, agents, (a, b) =>
        {
            if (a.ConnectionStatus != b.ConnectionStatus)
                return a.ConnectionStatus.CompareTo(b.ConnectionStatus);
            return string.Compare(a.FullyQualifiedDomainName, b.FullyQualifiedDomainName, StringComparison.Ordinal);
        });

        _logger.LogInformation("Applied agent list update (now={CurrentCount})", Agents.Count);

        if (Agents.Count < MaxAgents)
            ShowAllAgents = false;

        NotifyAgentCollectionStateChanged();

        var firstOnlineAgent = Agents.FirstOrDefault(a => a.ConnectionStatus != AgentConnectionStatus.Offline);
        if (firstOnlineAgent is null)
        {
            _hasExpandedAgent = false;
            _logger.LogInformation("No online agents after update");
        }

        if (!_hasExpandedAgent && firstOnlineAgent is not null)
        {
            _hasExpandedAgent = true;
            var agentToExpand = firstOnlineAgent;
            _logger.LogInformation("Queueing first online agent auto-expand for {AgentFqdn}", agentToExpand.FullyQualifiedDomainName);
            _dispatcher.Post(() =>
            {
                _logger.LogInformation("Running first online agent auto-expand for {AgentFqdn}", agentToExpand.FullyQualifiedDomainName);
                agentToExpand.SetExpanded(true);
            });

            _dispatcher.Post(() =>
            {
                _logger.LogInformation("UI pulse after agent update (agentsNow={AgentCount})", Agents.Count);
            });
        }

        _logger.LogInformation("UpdateFromRpcModel completed (vpn={Lifecycle}, agentsNow={AgentCount})", VpnLifecycle, Agents.Count);
    }

    private void UpdateFromCredentialModel(CredentialModel credentialModel)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => UpdateFromCredentialModel(credentialModel));
            return;
        }

        // CredentialModel updates trigger RpcStateModel updates first. This
        // resolves an issue on startup where the window would be locked for 5
        // seconds, even if all startup preconditions have been met:
        //
        // 1. RPC state updates, but credentials are invalid so the window
        //    enters the invalid loading state to prevent interaction.
        // 2. Credential model finally becomes valid after reaching out to the
        //    server to check credentials.
        // 3. UpdateFromCredentialModel previously did not re-trigger RpcModel
        //    update.
        // 4. Five seconds after step 1, a new RPC state update would come in
        //    and finally unlock the window.
        //
        // Calling UpdateFromRpcModel at step 3 resolves this issue.
        UpdateFromRpcModel(_rpcController.GetState());

        // HACK: the HyperlinkButton crashes the whole app if the initial URI
        // or this URI is invalid. CredentialModel.CoderUrl should never be
        // null while the Page is active as the Page is only displayed when
        // CredentialModel.Status == Valid.
        DashboardUrl = credentialModel.CoderUrl?.ToString() ?? DefaultDashboardUrl;
    }

    private void HandleHostnameSuffixChanged(string suffix)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => HandleHostnameSuffixChanged(suffix));
            return;
        }

        foreach (var agent in Agents)
        {
            agent.ConfiguredHostnameSuffix = suffix;
        }
    }

    partial void OnVpnSwitchActiveChanged(bool oldValue, bool newValue)
    {
        if (_settingVpnSwitchState)
            return;

        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => OnVpnSwitchActiveChanged(oldValue, newValue));
            return;
        }

        VpnFailedMessage = null;

        // The start/stop methods will call back to update the state.
        if (newValue && VpnLifecycle is VpnLifecycle.Stopped)
            _ = StartVpn(); // in the background
        else if (!newValue && VpnLifecycle is VpnLifecycle.Started)
            _ = StopVpn(); // in the background
        else
            SetVpnSwitchFromLifecycle();
    }

    private void SetVpnSwitchFromLifecycle()
    {
        _settingVpnSwitchState = true;
        try
        {
            VpnSwitchActive = VpnLifecycle is VpnLifecycle.Starting or VpnLifecycle.Started;
        }
        finally
        {
            _settingVpnSwitchState = false;
        }
    }

    private async Task StartVpn()
    {
        _logger.LogInformation("StartVpn requested");
        try
        {
            await _rpcController.StartVpn();
            _logger.LogInformation("StartVpn completed successfully");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "StartVpn failed");
            VpnFailedMessage = "Failed to start CoderVPN: " + MaybeUnwrapTunnelError(e);
        }
    }

    private async Task StopVpn()
    {
        _logger.LogInformation("StopVpn requested");
        try
        {
            await _rpcController.StopVpn();
            _logger.LogInformation("StopVpn completed successfully");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "StopVpn failed");
            VpnFailedMessage = "Failed to stop CoderVPN: " + MaybeUnwrapTunnelError(e);
        }
    }

    private static string MaybeUnwrapTunnelError(Exception e)
    {
        if (e is VpnLifecycleException vpnError) return vpnError.Message;
        return e.ToString();
    }

    [RelayCommand]
    private void ToggleShowAllAgents()
    {
        ShowAllAgents = !ShowAllAgents;
    }

    [RelayCommand]
    private void ShowFileSyncListWindow()
    {
        _windowService.ShowFileSyncListWindow();
    }

    [RelayCommand]
    private void ShowSettingsWindow()
    {
        _windowService.ShowSettingsWindow();
    }

    [RelayCommand]
    private async Task SignOut()
    {
        await _rpcController.StopVpn();
        await _credentialManager.ClearCredentials();
    }

    [RelayCommand]
    public void Exit()
    {
        _applicationLifetime.StopApplication();
    }

    private static bool ShouldShowDummy(Workspace workspace)
    {
        switch (workspace.Status)
        {
            case Workspace.Types.Status.Unknown:
            case Workspace.Types.Status.Pending:
            case Workspace.Types.Status.Starting:
            case Workspace.Types.Status.Stopping:
            case Workspace.Types.Status.Stopped:
                return true;
            // TODO: should we include and show a different color than Offline for workspaces that are
            // failed, canceled or deleting?
            default:
                return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.App.Views;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.Vpn.Proto;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Exception = System.Exception;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentExpanderHost
{
    public void HandleAgentExpanded(Uuid id, bool expanded);
}

public partial class TrayWindowViewModel : ObservableObject, IAgentExpanderHost
{
    private const int MaxAgents = 5;
    private const string DefaultDashboardUrl = "https://coder.com";
    private const string DefaultHostnameSuffix = ".coder";

    private readonly IServiceProvider _services;
    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;
    private readonly IAgentViewModelFactory _agentViewModelFactory;
    private readonly IHostnameSuffixGetter _hostnameSuffixGetter;

    private FileSyncListWindow? _fileSyncListWindow;

    private DispatcherQueue? _dispatcherQueue;

    // When we transition from 0 online workspaces to >0 online workspaces, the
    // first agent will be expanded. This bool tracks whether this has occurred
    // yet (or if the user has expanded something themselves).
    private bool _hasExpandedAgent;

    // This isn't an ObservableProperty because the property itself never
    // changes. We add an event listener for the collection changing in the
    // constructor.
    public readonly ObservableCollection<AgentViewModel> Agents = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnableSection))]
    [NotifyPropertyChangedFor(nameof(ShowWorkspacesHeader))]
    [NotifyPropertyChangedFor(nameof(ShowNoAgentsSection))]
    [NotifyPropertyChangedFor(nameof(ShowAgentsSection))]
    [NotifyPropertyChangedFor(nameof(SignOutButtonForeground))]
    [NotifyPropertyChangedFor(nameof(SignOutButtonTooltip))]
    public partial VpnLifecycle VpnLifecycle { get; set; } = VpnLifecycle.Unknown;

    // This is a separate property because we need the switch to be 2-way.
    [ObservableProperty] public partial bool VpnSwitchActive { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnableSection))]
    [NotifyPropertyChangedFor(nameof(ShowWorkspacesHeader))]
    [NotifyPropertyChangedFor(nameof(ShowNoAgentsSection))]
    [NotifyPropertyChangedFor(nameof(ShowAgentsSection))]
    [NotifyPropertyChangedFor(nameof(ShowAgentOverflowButton))]
    [NotifyPropertyChangedFor(nameof(ShowFailedSection))]
    public partial string? VpnFailedMessage { get; set; } = null;

    public bool ShowEnableSection => VpnFailedMessage is null && VpnLifecycle is not VpnLifecycle.Started;

    public bool ShowWorkspacesHeader => VpnFailedMessage is null && VpnLifecycle is VpnLifecycle.Started;

    public bool ShowNoAgentsSection =>
        VpnFailedMessage is null && Agents.Count == 0 && VpnLifecycle is VpnLifecycle.Started;

    public bool ShowAgentsSection =>
        VpnFailedMessage is null && Agents.Count > 0 && VpnLifecycle is VpnLifecycle.Started;

    public bool ShowFailedSection => VpnFailedMessage is not null;

    public bool ShowAgentOverflowButton => VpnFailedMessage is null && Agents.Count > MaxAgents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleAgents))]
    public partial bool ShowAllAgents { get; set; } = false;

    public IEnumerable<AgentViewModel> VisibleAgents => ShowAllAgents ? Agents : Agents.Take(MaxAgents);

    [ObservableProperty] public partial string DashboardUrl { get; set; } = DefaultDashboardUrl;

    public string SignOutButtonTooltip
    {
        get
        {
            return VpnLifecycle switch
            {
                VpnLifecycle.Stopped or VpnLifecycle.Unknown => "Sign out",
                _ => "Sign out (VPN must be stopped first)",
            };
        }
    }

    private Brush? _enabledForegroud;
    private Brush? _disabledForeground;

    public Brush SignOutButtonForeground
    {
        get
        {
            return VpnLifecycle switch
            {
                VpnLifecycle.Stopped or VpnLifecycle.Unknown => _enabledForegroud ?? new SolidColorBrush(Colors.White),
                _ => _disabledForeground ?? new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)),
            };
        }
    }
    public static Brush? FindBrushByName(string brushName)
    {
        if (Application.Current.Resources.TryGetValue(brushName, out var resource) && resource is Brush brush)
        {
            return brush;
        }

        return null;
    }

    public TrayWindowViewModel(IServiceProvider services, IRpcController rpcController,
        ICredentialManager credentialManager, IAgentViewModelFactory agentViewModelFactory, IHostnameSuffixGetter hostnameSuffixGetter)
    {
        _services = services;
        _rpcController = rpcController;
        _credentialManager = credentialManager;
        _agentViewModelFactory = agentViewModelFactory;
        _hostnameSuffixGetter = hostnameSuffixGetter;
        _disabledForeground = FindBrushByName("SystemControlForegroundBaseMediumBrush");
        _enabledForegroud = FindBrushByName("DefaultTextForegroundThemeBrush");



        // Since the property value itself never changes, we add event
        // listeners for the underlying collection changing instead.
        Agents.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(VisibleAgents)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowNoAgentsSection)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowAgentsSection)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowAgentOverflowButton)));
        };
    }

    // Implements IAgentExpanderHost
    public void HandleAgentExpanded(Uuid id, bool expanded)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => HandleAgentExpanded(id, expanded));
            return;
        }

        if (!expanded) return;
        _hasExpandedAgent = true;
        // Collapse every other agent.
        foreach (var otherAgent in Agents.Where(a => a.Id != id))
            otherAgent.SetExpanded(false);
    }

    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        _rpcController.StateChanged += (_, rpcModel) => UpdateFromRpcModel(rpcModel);
        UpdateFromRpcModel(_rpcController.GetState());

        _credentialManager.CredentialsChanged += (_, credentialModel) => UpdateFromCredentialModel(credentialModel);
        UpdateFromCredentialModel(_credentialManager.GetCachedCredentials());

        _hostnameSuffixGetter.SuffixChanged += (_, suffix) => HandleHostnameSuffixChanged(suffix);
        HandleHostnameSuffixChanged(_hostnameSuffixGetter.GetCachedSuffix());
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

        // As a failsafe, if RPC is disconnected (or we're not signed in) we
        // disable the switch. The Window should not show the current Page if
        // the RPC is disconnected.
        var credentialModel = _credentialManager.GetCachedCredentials();
        if (rpcModel.RpcLifecycle is RpcLifecycle.Disconnected || credentialModel.State is not CredentialState.Valid ||
            credentialModel.CoderUrl == null)
        {
            VpnLifecycle = VpnLifecycle.Unknown;
            VpnSwitchActive = false;
            Agents.Clear();
            return;
        }

        VpnLifecycle = rpcModel.VpnLifecycle;
        VpnSwitchActive = rpcModel.VpnLifecycle is VpnLifecycle.Starting or VpnLifecycle.Started;

        // Add every known agent.
        HashSet<ByteString> workspacesWithAgents = [];
        List<AgentViewModel> agents = [];
        foreach (var agent in rpcModel.Agents)
        {
            if (!Uuid.TryFrom(agent.Id.Span, out var uuid))
                continue;

            // Find the FQDN with the least amount of dots and split it into
            // prefix and suffix.
            var fqdn = agent.Fqdn
                .Select(a => a.Trim('.'))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Aggregate((a, b) => a.Count(c => c == '.') < b.Count(c => c == '.') ? a : b);
            if (string.IsNullOrWhiteSpace(fqdn))
                continue;

            var lastHandshakeAgo = DateTime.UtcNow.Subtract(agent.LastHandshake.ToDateTime());
            var connectionStatus = lastHandshakeAgo < TimeSpan.FromMinutes(5)
                ? AgentConnectionStatus.Green
                : AgentConnectionStatus.Yellow;
            workspacesWithAgents.Add(agent.WorkspaceId);
            var workspace = rpcModel.Workspaces.FirstOrDefault(w => w.Id == agent.WorkspaceId);

            agents.Add(_agentViewModelFactory.Create(
                this,
                uuid,
                fqdn,
                _hostnameSuffixGetter.GetCachedSuffix(),
                connectionStatus,
                credentialModel.CoderUrl,
                workspace?.Name));
        }

        // For every stopped workspace that doesn't have any agents, add a
        // dummy agent row.
        foreach (var workspace in rpcModel.Workspaces.Where(w =>
                     w.Status == Workspace.Types.Status.Stopped && !workspacesWithAgents.Contains(w.Id)))
        {
            if (!Uuid.TryFrom(workspace.Id.Span, out var uuid))
                continue;

            agents.Add(_agentViewModelFactory.CreateDummy(
                this,
                // Workspace ID is fine as a stand-in here, it shouldn't
                // conflict with any agent IDs.
                uuid,
                _hostnameSuffixGetter.GetCachedSuffix(),
                AgentConnectionStatus.Gray,
                credentialModel.CoderUrl,
                workspace.Name));
        }

        // Sort by status green, red, gray, then by hostname.
        ModelUpdate.ApplyLists(Agents, agents, (a, b) =>
        {
            if (a.ConnectionStatus != b.ConnectionStatus)
                return a.ConnectionStatus.CompareTo(b.ConnectionStatus);
            return string.Compare(a.FullyQualifiedDomainName, b.FullyQualifiedDomainName, StringComparison.Ordinal);
        });

        if (Agents.Count < MaxAgents) ShowAllAgents = false;

        var firstOnlineAgent = agents.FirstOrDefault(a => a.ConnectionStatus != AgentConnectionStatus.Gray);
        if (firstOnlineAgent is null)
            _hasExpandedAgent = false;
        if (!_hasExpandedAgent && firstOnlineAgent is not null)
        {
            firstOnlineAgent.SetExpanded(true);
            _hasExpandedAgent = true;
        }
    }

    private void UpdateFromCredentialModel(CredentialModel credentialModel)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateFromCredentialModel(credentialModel));
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
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => HandleHostnameSuffixChanged(suffix));
            return;
        }

        foreach (var agent in Agents)
        {
            agent.ConfiguredHostnameSuffix = suffix;
        }
    }

    public void VpnSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch) return;

        VpnFailedMessage = null;

        // The start/stop methods will call back to update the state.
        if (toggleSwitch.IsOn && VpnLifecycle is VpnLifecycle.Stopped)
            _ = StartVpn(); // in the background
        else if (!toggleSwitch.IsOn && VpnLifecycle is VpnLifecycle.Started)
            _ = StopVpn(); // in the background
        else
            toggleSwitch.IsOn = VpnLifecycle is VpnLifecycle.Starting or VpnLifecycle.Started;
    }

    private async Task StartVpn()
    {
        try
        {
            await _rpcController.StartVpn();
        }
        catch (Exception e)
        {
            VpnFailedMessage = "Failed to start CoderVPN: " + MaybeUnwrapTunnelError(e);
        }
    }

    private async Task StopVpn()
    {
        try
        {
            await _rpcController.StopVpn();
        }
        catch (Exception e)
        {
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
        // This is safe against concurrent access since it all happens in the
        // UI thread.
        if (_fileSyncListWindow != null)
        {
            _fileSyncListWindow.Activate();
            return;
        }

        _fileSyncListWindow = _services.GetRequiredService<FileSyncListWindow>();
        _fileSyncListWindow.Closed += (_, _) => _fileSyncListWindow = null;
        _fileSyncListWindow.Activate();
    }

    [RelayCommand]
    private void SignOut()
    {
        if (VpnLifecycle is not VpnLifecycle.Stopped)
            return;
        _credentialManager.ClearCredentials();
    }

    [RelayCommand]
    public void Exit()
    {
        _ = ((App)Application.Current).ExitApplication();
    }
}

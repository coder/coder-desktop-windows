using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.CoderSdk.Coder;
using Coder.Desktop.Vpn.Proto;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentViewModelFactory
{
    public AgentViewModel Create(IAgentExpanderHost expanderHost, Uuid id, string fullyQualifiedDomainName,
        string hostnameSuffix, AgentConnectionStatus connectionStatus, Uri dashboardBaseUrl,
        string? workspaceName, bool? didP2p, string? preferredDerp, TimeSpan? latency, TimeSpan? preferredDerpLatency, DateTime? lastHandshake);
    public AgentViewModel CreateDummy(IAgentExpanderHost expanderHost, Uuid id,
        string hostnameSuffix,
        AgentConnectionStatus connectionStatus, Uri dashboardBaseUrl, string workspaceName);
}

public class AgentViewModelFactory(
    ILogger<AgentViewModel> childLogger,
    ICoderApiClientFactory coderApiClientFactory,
    ICredentialManager credentialManager,
    IAgentAppViewModelFactory agentAppViewModelFactory) : IAgentViewModelFactory
{
    public AgentViewModel Create(IAgentExpanderHost expanderHost, Uuid id, string fullyQualifiedDomainName,
        string hostnameSuffix,
        AgentConnectionStatus connectionStatus, Uri dashboardBaseUrl,
        string? workspaceName, bool? didP2p, string? preferredDerp, TimeSpan? latency, TimeSpan? preferredDerpLatency,
        DateTime? lastHandshake)
    {
        System.Diagnostics.Debug.WriteLine($"Creating agent: {didP2p} {preferredDerp} {latency} {lastHandshake}");
        return new AgentViewModel(childLogger, coderApiClientFactory, credentialManager, agentAppViewModelFactory,
            expanderHost, id)
        {
            ConfiguredFqdn = fullyQualifiedDomainName,
            ConfiguredHostname = string.Empty,
            ConfiguredHostnameSuffix = hostnameSuffix,
            ConnectionStatus = connectionStatus,
            DashboardBaseUrl = dashboardBaseUrl,
            WorkspaceName = workspaceName,
            DidP2p = didP2p,
            PreferredDerp = preferredDerp,
            Latency = latency,
            PreferredDerpLatency = preferredDerpLatency,
            LastHandshake = lastHandshake,
        };
    }

    public AgentViewModel CreateDummy(IAgentExpanderHost expanderHost, Uuid id,
        string hostnameSuffix,
        AgentConnectionStatus connectionStatus, Uri dashboardBaseUrl, string workspaceName)
    {
        return new AgentViewModel(childLogger, coderApiClientFactory, credentialManager, agentAppViewModelFactory,
            expanderHost, id)
        {
            ConfiguredFqdn = string.Empty,
            ConfiguredHostname = workspaceName,
            ConfiguredHostnameSuffix = hostnameSuffix,
            ConnectionStatus = connectionStatus,
            DashboardBaseUrl = dashboardBaseUrl,
            WorkspaceName = workspaceName,
        };
    }
}

public enum AgentConnectionStatus
{
    Healthy,
    Unhealthy,
    NoRecentHandshake,
    Offline,
}

public partial class AgentViewModel : ObservableObject, IModelUpdateable<AgentViewModel>
{
    private const string DefaultDashboardUrl = "https://coder.com";
    private const int MaxAppsPerRow = 6;

    // These are fake UUIDs, for UI purposes only. Display apps don't exist on
    // the backend as real app resources and therefore don't have an ID.
    private static readonly Uuid VscodeAppUuid = new("819828b1-5213-4c3d-855e-1b74db6ddd19");
    private static readonly Uuid VscodeInsidersAppUuid = new("becf1e10-5101-4940-a853-59af86468069");

    private readonly ILogger<AgentViewModel> _logger;
    private readonly ICoderApiClientFactory _coderApiClientFactory;
    private readonly ICredentialManager _credentialManager;
    private readonly IAgentAppViewModelFactory _agentAppViewModelFactory;

    // The AgentViewModel only gets created on the UI thread.
    private readonly DispatcherQueue _dispatcherQueue =
        DispatcherQueue.GetForCurrentThread();

    private readonly IAgentExpanderHost _expanderHost;

    // This isn't an ObservableProperty because the property itself never
    // changes. We add an event listener for the collection changing in the
    // constructor.
    public readonly ObservableCollection<AgentAppViewModel> Apps = [];

    public readonly Uuid Id;

    // This is set only for "dummy" agents that represent unstarted workspaces. If set, then ConfiguredFqdn
    // should be empty, otherwise it will override this.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewableHostname))]
    [NotifyPropertyChangedFor(nameof(ViewableHostnameSuffix))]
    [NotifyPropertyChangedFor(nameof(FullyQualifiedDomainName))]
    public required partial string ConfiguredHostname { get; set; }

    // This should be set if we have an FQDN from the VPN service, and overrides ConfiguredHostname if set.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewableHostname))]
    [NotifyPropertyChangedFor(nameof(ViewableHostnameSuffix))]
    [NotifyPropertyChangedFor(nameof(FullyQualifiedDomainName))]
    public required partial string ConfiguredFqdn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewableHostname))]
    [NotifyPropertyChangedFor(nameof(ViewableHostnameSuffix))]
    [NotifyPropertyChangedFor(nameof(FullyQualifiedDomainName))]
    public required partial string ConfiguredHostnameSuffix { get; set; } // including leading dot


    public string FullyQualifiedDomainName
    {
        get
        {
            if (!string.IsNullOrEmpty(ConfiguredFqdn)) return ConfiguredFqdn;
            return ConfiguredHostname + ConfiguredHostnameSuffix;
        }
    }

    /// <summary>
    /// ViewableHostname is the hostname portion of the fully qualified domain name (FQDN) specifically for
    /// views that render it differently than the suffix. If the ConfiguredHostnameSuffix doesn't actually
    /// match the FQDN, then this will be the entire FQDN, and ViewableHostnameSuffix will be empty.
    /// </summary>
    public string ViewableHostname => !FullyQualifiedDomainName.EndsWith(ConfiguredHostnameSuffix)
        ? FullyQualifiedDomainName
        : FullyQualifiedDomainName[0..^ConfiguredHostnameSuffix.Length];

    /// <summary>
    /// ViewableHostnameSuffix is the domain suffix portion (including leading dot) of the fully qualified
    /// domain name (FQDN) specifically for views that render it differently from the rest of the FQDN. If
    /// the ConfiguredHostnameSuffix doesn't actually match the FQDN, then this will be empty and the
    /// ViewableHostname will contain the entire FQDN.
    /// </summary>
    public string ViewableHostnameSuffix => FullyQualifiedDomainName.EndsWith(ConfiguredHostnameSuffix)
        ? ConfiguredHostnameSuffix
        : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpandAppsMessage))]
    [NotifyPropertyChangedFor(nameof(ExpandAppsMessage))]
    public required partial AgentConnectionStatus ConnectionStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DashboardUrl))]
    public required partial Uri DashboardBaseUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DashboardUrl))]
    public required partial string? WorkspaceName { get; set; }

    [ObservableProperty] public partial bool IsExpanded { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpandAppsMessage))]
    [NotifyPropertyChangedFor(nameof(ExpandAppsMessage))]
    public partial bool FetchingApps { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpandAppsMessage))]
    [NotifyPropertyChangedFor(nameof(ExpandAppsMessage))]
    public partial bool AppFetchErrored { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionTooltip))]
    public partial bool? DidP2p { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionTooltip))]
    public partial string? PreferredDerp { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionTooltip))]
    public partial TimeSpan? Latency { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionTooltip))]
    public partial TimeSpan? PreferredDerpLatency { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionTooltip))]
    public partial DateTime? LastHandshake { get; set; } = null;

    public string ConnectionTooltip { get
        {
            var description = new StringBuilder();

            if (DidP2p != null && DidP2p.Value && Latency != null)
            {
                description.Append($"""
                You're connected peer-to-peer.

                You ↔ {Latency.Value.Milliseconds} ms ↔ {WorkspaceName}
                """
                );
            }
            else if (PreferredDerpLatency != null)
            {
                description.Append($"""
                You're connected through a DERP relay.
                We'll switch over to peer-to-peer when available.

                Total latency: {PreferredDerpLatency.Value.Milliseconds} ms
                """
                );

                if (PreferredDerp != null && Latency != null)
                {
                    description.Append($"\nYou ↔ {PreferredDerp}: {PreferredDerpLatency.Value.Milliseconds} ms");

                    var derpToWorkspaceEstimatedLatency = Latency - PreferredDerpLatency;

                    // Guard against negative values if the two readings were taken at different times
                    if (derpToWorkspaceEstimatedLatency > TimeSpan.Zero)
                    {
                        description.Append($"\n{PreferredDerp} ms ↔ {WorkspaceName}: {derpToWorkspaceEstimatedLatency.Value.Milliseconds} ms");
                    }
                }
            }
            if (LastHandshake != null)
                description.Append($"\n\nLast handshake: {LastHandshake?.ToString() ?? "Unknown"}");

            var tooltip = description.ToString().TrimEnd('\n', ' ');

            if (string.IsNullOrEmpty(tooltip))
                return "No connection information available.";

            return tooltip;
        }
    }


    // We only show 6 apps max, which fills the entire width of the tray
    // window.
    public IEnumerable<AgentAppViewModel> VisibleApps => Apps.Count > MaxAppsPerRow ? Apps.Take(MaxAppsPerRow) : Apps;

    public bool ShowExpandAppsMessage => ExpandAppsMessage != null;

    public string? ExpandAppsMessage
    {
        get
        {
            if (ConnectionStatus == AgentConnectionStatus.Offline)
                return "Your workspace is offline.";
            if (FetchingApps && Apps.Count == 0)
                // Don't show this message if we have any apps already. When
                // they finish loading, we'll just update the screen with any
                // changes.
                return "Fetching workspace apps...";
            if (AppFetchErrored && Apps.Count == 0)
                // There's very limited screen real estate here so we don't
                // show the actual error message.
                return "Could not fetch workspace apps.";
            if (Apps.Count == 0)
                return "No apps to show.";
            return null;
        }
    }

    public string DashboardUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(WorkspaceName)) return DashboardBaseUrl.ToString();
            try
            {
                return new Uri(DashboardBaseUrl, $"/@me/{WorkspaceName}").ToString();
            }
            catch
            {
                return DefaultDashboardUrl;
            }
        }
    }

    public AgentViewModel(ILogger<AgentViewModel> logger, ICoderApiClientFactory coderApiClientFactory,
        ICredentialManager credentialManager, IAgentAppViewModelFactory agentAppViewModelFactory,
        IAgentExpanderHost expanderHost, Uuid id)
    {
        _logger = logger;
        _coderApiClientFactory = coderApiClientFactory;
        _credentialManager = credentialManager;
        _agentAppViewModelFactory = agentAppViewModelFactory;
        _expanderHost = expanderHost;

        Id = id;

        PropertyChanging += (x, args) =>
        {
            if (args.PropertyName == nameof(IsExpanded))
            {
                var value = !IsExpanded;
                if (value)
                    _expanderHost.HandleAgentExpanded(Id, value);
            }
        };

        PropertyChanged += (x, args) =>
        {
            if (args.PropertyName == nameof(IsExpanded))
            {
                // Every time the drawer is expanded, re-fetch all apps.
                if (IsExpanded && !FetchingApps)
                    FetchApps();
            }
        };

        // Since the property value itself never changes, we add event
        // listeners for the underlying collection changing instead.
        Apps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(VisibleApps)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowExpandAppsMessage)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ExpandAppsMessage)));
        };
    }

    public bool TryApplyChanges(AgentViewModel model)
    {
        if (Id != model.Id) return false;

        // To avoid spurious UI updates which cause flashing, don't actually
        // write to values unless they've changed.
        if (ConfiguredFqdn != model.ConfiguredFqdn)
            ConfiguredFqdn = model.ConfiguredFqdn;
        if (ConfiguredHostname != model.ConfiguredHostname)
            ConfiguredHostname = model.ConfiguredHostname;
        if (ConfiguredHostnameSuffix != model.ConfiguredHostnameSuffix)
            ConfiguredHostnameSuffix = model.ConfiguredHostnameSuffix;
        if (ConnectionStatus != model.ConnectionStatus)
            ConnectionStatus = model.ConnectionStatus;
        if (DashboardBaseUrl != model.DashboardBaseUrl)
            DashboardBaseUrl = model.DashboardBaseUrl;
        if (WorkspaceName != model.WorkspaceName)
            WorkspaceName = model.WorkspaceName;
        if (DidP2p != model.DidP2p)
            DidP2p = model.DidP2p;
        if (PreferredDerp != model.PreferredDerp)
            PreferredDerp = model.PreferredDerp;
        if (Latency != model.Latency)
            Latency = model.Latency;
        if (PreferredDerpLatency != model.PreferredDerpLatency)
            PreferredDerpLatency = model.PreferredDerpLatency;
        if (LastHandshake != model.LastHandshake)
            LastHandshake = model.LastHandshake;

        // Apps are not set externally.

        return true;
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        SetExpanded(!IsExpanded);
    }

    public void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        // This will bubble up to the TrayWindowViewModel because of the
        // PropertyChanged handler.
        IsExpanded = expanded;
    }

    partial void OnConnectionStatusChanged(AgentConnectionStatus oldValue, AgentConnectionStatus newValue)
    {
        if (IsExpanded && newValue is not AgentConnectionStatus.Offline) FetchApps();
    }

    private void FetchApps()
    {
        if (FetchingApps) return;
        FetchingApps = true;

        // If the workspace is off, then there's no agent and there's no apps.
        if (ConnectionStatus == AgentConnectionStatus.Offline)
        {
            FetchingApps = false;
            Apps.Clear();
            return;
        }

        // API client creation could fail, which would leave FetchingApps true.
        ICoderApiClient client;
        try
        {
            client = _coderApiClientFactory.Create(_credentialManager);
        }
        catch
        {
            FetchingApps = false;
            throw;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        client.GetWorkspaceAgent(Id.ToString(), cts.Token).ContinueWith(t =>
        {
            cts.Dispose();
            ContinueFetchApps(t);
        }, CancellationToken.None);
    }

    private void ContinueFetchApps(Task<WorkspaceAgent> task)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ContinueFetchApps(task));
            return;
        }

        FetchingApps = false;
        AppFetchErrored = !task.IsCompletedSuccessfully;
        if (!task.IsCompletedSuccessfully)
        {
            _logger.LogWarning(task.Exception, "Could not fetch workspace agent");
            return;
        }

        var workspaceAgent = task.Result;
        var apps = new List<AgentAppViewModel>();
        foreach (var app in workspaceAgent.Apps)
        {
            if (!app.External || !string.IsNullOrEmpty(app.Command)) continue;

            if (!Uri.TryCreate(app.Url, UriKind.Absolute, out var appUri))
            {
                _logger.LogWarning("Could not parse app URI '{Url}' for '{DisplayName}', app will not appear in list",
                    app.Url,
                    app.DisplayName);
                continue;
            }

            // HTTP or HTTPS external apps are usually things like
            // wikis/documentation, which clutters up the app.
            if (appUri.Scheme is "http" or "https")
                continue;

            // Icon parse failures are not fatal, we will just use the fallback
            // icon.
            _ = Uri.TryCreate(DashboardBaseUrl, app.Icon, out var iconUrl);

            apps.Add(_agentAppViewModelFactory.Create(app.Id, app.DisplayName, appUri, iconUrl));
        }

        foreach (var displayApp in workspaceAgent.DisplayApps)
        {
            if (displayApp is not WorkspaceAgent.DisplayAppVscode and not WorkspaceAgent.DisplayAppVscodeInsiders)
                continue;

            var id = VscodeAppUuid;
            var displayName = "VS Code";
            var icon = "/icon/code.svg";
            var scheme = "vscode";
            if (displayApp is WorkspaceAgent.DisplayAppVscodeInsiders)
            {
                id = VscodeInsidersAppUuid;
                displayName = "VS Code Insiders";
                icon = "/icon/code-insiders.svg";
                scheme = "vscode-insiders";
            }

            Uri appUri;
            try
            {
                appUri = new UriBuilder
                {
                    Scheme = scheme,
                    Host = "vscode-remote",
                    Path = $"/ssh-remote+{FullyQualifiedDomainName}/{workspaceAgent.ExpandedDirectory}",
                }.Uri;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e,
                    "Could not craft app URI for display app {displayApp}, app will not appear in list",
                    displayApp);
                continue;
            }

            // Icon parse failures are not fatal, we will just use the fallback
            // icon.
            _ = Uri.TryCreate(DashboardBaseUrl, icon, out var iconUrl);

            apps.Add(_agentAppViewModelFactory.Create(id, displayName, appUri, iconUrl));
        }

        // Sort by name.
        ModelUpdate.ApplyLists(Apps, apps, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    [RelayCommand]
    private void CopyHostname(object parameter)
    {
        var dataPackage = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy,
        };
        dataPackage.SetText(FullyQualifiedDomainName);
        Clipboard.SetContent(dataPackage);

        if (parameter is not FrameworkElement frameworkElement) return;

        var flyout = new Flyout
        {
            Content = new TextBlock
            {
                Text = "DNS Copied",
                Margin = new Thickness(4),
            },
        };
        FlyoutBase.SetAttachedFlyout(frameworkElement, flyout);
        FlyoutBase.ShowAttachedFlyout(frameworkElement);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.CoderSdk.Coder;
using Coder.Desktop.Vpn.Proto;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentViewModelFactory
{
    public AgentViewModel Create(IAgentExpanderHost expanderHost, Uuid id, string hostname, string hostnameSuffix,
        AgentConnectionStatus connectionStatus, Uri dashboardBaseUrl, string? workspaceName);
}

public class AgentViewModelFactory(
    ILogger<AgentViewModel> childLogger,
    ICoderApiClientFactory coderApiClientFactory,
    ICredentialManager credentialManager,
    IAgentAppViewModelFactory agentAppViewModelFactory) : IAgentViewModelFactory
{
    public AgentViewModel Create(IAgentExpanderHost expanderHost, Uuid id, string hostname, string hostnameSuffix,
        AgentConnectionStatus connectionStatus, Uri dashboardBaseUrl, string? workspaceName)
    {
        return new AgentViewModel(childLogger, coderApiClientFactory, credentialManager, agentAppViewModelFactory,
            expanderHost, id)
        {
            Hostname = hostname,
            HostnameSuffix = hostnameSuffix,
            ConnectionStatus = connectionStatus,
            DashboardBaseUrl = dashboardBaseUrl,
            WorkspaceName = workspaceName,
        };
    }
}

public enum AgentConnectionStatus
{
    Green,
    Yellow,
    Red,
    Gray,
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullHostname))]
    public required partial string Hostname { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullHostname))]
    public required partial string HostnameSuffix { get; set; } // including leading dot

    public string FullHostname => Hostname + HostnameSuffix;

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

    // We only show 6 apps max, which fills the entire width of the tray
    // window.
    public IEnumerable<AgentAppViewModel> VisibleApps => Apps.Count > MaxAppsPerRow ? Apps.Take(MaxAppsPerRow) : Apps;

    public bool ShowExpandAppsMessage => ExpandAppsMessage != null;

    public string? ExpandAppsMessage
    {
        get
        {
            if (ConnectionStatus == AgentConnectionStatus.Gray)
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

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IsExpanded))
            {
                _expanderHost.HandleAgentExpanded(Id, IsExpanded);

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
        if (Hostname != model.Hostname)
            Hostname = model.Hostname;
        if (HostnameSuffix != model.HostnameSuffix)
            HostnameSuffix = model.HostnameSuffix;
        if (ConnectionStatus != model.ConnectionStatus)
            ConnectionStatus = model.ConnectionStatus;
        if (DashboardBaseUrl != model.DashboardBaseUrl)
            DashboardBaseUrl = model.DashboardBaseUrl;
        if (WorkspaceName != model.WorkspaceName)
            WorkspaceName = model.WorkspaceName;

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
        if (IsExpanded && newValue is not AgentConnectionStatus.Gray) FetchApps();
    }

    private void FetchApps()
    {
        if (FetchingApps) return;
        FetchingApps = true;

        // If the workspace is off, then there's no agent and there's no apps.
        if (ConnectionStatus == AgentConnectionStatus.Gray)
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

            if (!Uuid.TryParse(app.Id, out var uuid))
            {
                _logger.LogWarning("Could not parse app UUID '{Id}' for '{DisplayName}', app will not appear in list",
                    app.Id, app.DisplayName);
                continue;
            }

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

            apps.Add(_agentAppViewModelFactory.Create(uuid, app.DisplayName, appUri, iconUrl));
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
                    Path = $"/ssh-remote+{FullHostname}/{workspaceAgent.ExpandedDirectory}",
                }.Uri;
            }
            catch (Exception e)
            {
                _logger.LogWarning("Could not craft app URI for display app {displayApp}, app will not appear in list",
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
        dataPackage.SetText(FullHostname);
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

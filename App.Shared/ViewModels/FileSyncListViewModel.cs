using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.CoderSdk.Agent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public partial class FileSyncListViewModel : ObservableObject
{
    private readonly ISyncSessionController _syncSessionController;
    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;
    private readonly IAgentApiClientFactory _agentApiClientFactory;
    private readonly IDispatcher _dispatcher;
    private readonly IWindowService _windowService;

    /// <summary>
    /// View model for the remote directory picker dialog (if open).
    ///
    /// Replaces the WinUI DirectoryPickerWindow.
    /// </summary>
    [ObservableProperty]
    private DirectoryPickerViewModel? _remotePathPickerViewModel = null;

    /// <summary>
    /// UI hook for opening a local folder picker.
    ///
    /// The WinUI implementation used Windows.Storage.Pickers.FolderPicker.
    /// </summary>
    public Func<Task<string?>>? LocalFolderPicker { get; set; }

    /// <summary>
    /// Optional UI hook for confirming termination of a sync session.
    ///
    /// If not set, termination will proceed without confirmation.
    /// </summary>
    public Func<string, Task<bool>>? ConfirmTerminateSessionAsync { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    private string? _unavailableMessage = null;

    // Initially we use the current cached state, the loading screen is only
    // shown when the user clicks "Reload" on the error screen.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    private bool _loading = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    private string? _error = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenLocalPath))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemoteHostEnabled))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemotePathDialogEnabled))]
    private bool _operationInProgress = false;

    [ObservableProperty]
    private IReadOnlyList<SyncSessionViewModel> _sessions = [];

    [ObservableProperty]
    private bool _creatingNewSession = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    private string _newSessionLocalPath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    [NotifyPropertyChangedFor(nameof(CanOpenLocalPath))]
    private bool _newSessionLocalPathDialogOpen = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionRemoteHostEnabled))]
    private IReadOnlyList<string> _availableHosts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemotePathDialogEnabled))]
    private string? _newSessionRemoteHost = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    private string _newSessionRemotePath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemotePathDialogEnabled))]
    private bool _newSessionRemotePathDialogOpen = false;

    public bool CanOpenLocalPath => !NewSessionLocalPathDialogOpen && !OperationInProgress;

    public bool NewSessionRemoteHostEnabled => AvailableHosts.Count > 0 && !OperationInProgress;

    public bool NewSessionRemotePathDialogEnabled =>
        !string.IsNullOrWhiteSpace(NewSessionRemoteHost) && !NewSessionRemotePathDialogOpen && !OperationInProgress;

    [ObservableProperty]
    private string _newSessionStatus = "";

    public bool NewSessionCreateEnabled
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NewSessionLocalPath)) return false;
            if (NewSessionLocalPathDialogOpen) return false;
            if (string.IsNullOrWhiteSpace(NewSessionRemoteHost)) return false;
            if (string.IsNullOrWhiteSpace(NewSessionRemotePath)) return false;
            if (NewSessionRemotePathDialogOpen) return false;
            return true;
        }
    }

    // TODO: this could definitely be improved
    public bool ShowUnavailable => UnavailableMessage != null;
    public bool ShowLoading => Loading && UnavailableMessage == null && Error == null;
    public bool ShowError => UnavailableMessage == null && Error != null;
    public bool ShowSessions => !Loading && UnavailableMessage == null && Error == null;

    public FileSyncListViewModel(ISyncSessionController syncSessionController, IRpcController rpcController,
        ICredentialManager credentialManager, IAgentApiClientFactory agentApiClientFactory,
        IDispatcher dispatcher, IWindowService windowService)
    {
        _syncSessionController = syncSessionController;
        _rpcController = rpcController;
        _credentialManager = credentialManager;
        _agentApiClientFactory = agentApiClientFactory;
        _dispatcher = dispatcher;
        _windowService = windowService;

        _rpcController.StateChanged += RpcControllerStateChanged;
        _credentialManager.CredentialsChanged += CredentialManagerCredentialsChanged;
        _syncSessionController.StateChanged += SyncSessionStateChanged;

        var rpcModel = _rpcController.GetState();
        var credentialModel = _credentialManager.GetCachedCredentials();
        var syncSessionState = _syncSessionController.GetState();

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() =>
            {
                UpdateSyncSessionState(syncSessionState);
                MaybeSetUnavailableMessage(rpcModel, credentialModel, syncSessionState);
            });
            return;
        }

        UpdateSyncSessionState(syncSessionState);
        MaybeSetUnavailableMessage(rpcModel, credentialModel, syncSessionState);
    }

    public void Dispose()
    {
        CloseRemotePathPicker();

        _rpcController.StateChanged -= RpcControllerStateChanged;
        _credentialManager.CredentialsChanged -= CredentialManagerCredentialsChanged;
        _syncSessionController.StateChanged -= SyncSessionStateChanged;
    }

    private void RpcControllerStateChanged(object? sender, RpcModel rpcModel)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => RpcControllerStateChanged(sender, rpcModel));
            return;
        }

        var credentialModel = _credentialManager.GetCachedCredentials();
        var syncSessionState = _syncSessionController.GetState();
        MaybeSetUnavailableMessage(rpcModel, credentialModel, syncSessionState);
    }

    private void CredentialManagerCredentialsChanged(object? sender, CredentialModel credentialModel)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => CredentialManagerCredentialsChanged(sender, credentialModel));
            return;
        }

        var rpcModel = _rpcController.GetState();
        var syncSessionState = _syncSessionController.GetState();
        MaybeSetUnavailableMessage(rpcModel, credentialModel, syncSessionState);
    }

    private void SyncSessionStateChanged(object? sender, SyncSessionControllerStateModel syncSessionState)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => SyncSessionStateChanged(sender, syncSessionState));
            return;
        }

        UpdateSyncSessionState(syncSessionState);
    }

    private void MaybeSetUnavailableMessage(RpcModel rpcModel, CredentialModel credentialModel, SyncSessionControllerStateModel syncSessionState)
    {
        var oldMessage = UnavailableMessage;
        if (rpcModel.RpcLifecycle != RpcLifecycle.Connected)
        {
            UnavailableMessage =
                "Disconnected from the Windows service. Please see the tray window for more information.";
        }
        else if (credentialModel.State != CredentialState.Valid)
        {
            UnavailableMessage = "Please sign in to access file sync.";
        }
        else if (rpcModel.VpnLifecycle != VpnLifecycle.Started)
        {
            UnavailableMessage = "Please start Coder Connect from the tray window to access file sync.";
        }
        else if (syncSessionState.Lifecycle == SyncSessionControllerLifecycle.Uninitialized)
        {
            UnavailableMessage = "Sync session controller is not initialized. Please wait...";
        }
        else
        {
            UnavailableMessage = null;
            // Reload if we transitioned from unavailable to available.
            if (oldMessage != null) ReloadSessions();
        }

        // When transitioning from available to unavailable:
        if (oldMessage == null && UnavailableMessage != null)
            ClearNewForm();
    }

    private void UpdateSyncSessionState(SyncSessionControllerStateModel syncSessionState)
    {
        // This should never happen.
        if (syncSessionState == null)
            return;
        if (syncSessionState.Lifecycle == SyncSessionControllerLifecycle.Uninitialized)
        {
            MaybeSetUnavailableMessage(_rpcController.GetState(), _credentialManager.GetCachedCredentials(), syncSessionState);
        }
        Error = syncSessionState.DaemonError;
        Sessions = syncSessionState.SyncSessions.Select(s => new SyncSessionViewModel(this, s)).ToList();
    }

    private void ClearNewForm()
    {
        CreatingNewSession = false;
        NewSessionLocalPath = "";
        NewSessionRemoteHost = "";
        NewSessionRemotePath = "";
        NewSessionStatus = "";
        CloseRemotePathPicker();
    }

    [RelayCommand]
    private void ReloadSessions()
    {
        Loading = true;
        Error = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _syncSessionController.RefreshState(cts.Token).ContinueWith(HandleRefresh, CancellationToken.None);
    }

    private void HandleRefresh(Task<SyncSessionControllerStateModel> t)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => HandleRefresh(t));
            return;
        }

        if (t.IsCompletedSuccessfully)
        {
            Sessions = t.Result.SyncSessions.Select(s => new SyncSessionViewModel(this, s)).ToList();
            Loading = false;
            Error = t.Result.DaemonError;
            return;
        }

        Error = "Could not list sync sessions: ";
        if (t.IsCanceled) Error += new TaskCanceledException();
        else if (t.IsFaulted) Error += t.Exception;
        else Error += "no successful result or error";
        Loading = false;
    }

    // Overriding AvailableHosts seems to make the ComboBox clear its value, so
    // we only do this while the create form is not open.
    // Must be called in UI thread.
    private void SetAvailableHostsFromRpcModel(RpcModel rpcModel)
    {
        var hosts = new List<string>(rpcModel.Agents.Count);
        // Agents will only contain started agents.
        foreach (var agent in rpcModel.Agents)
        {
            var fqdn = agent.Fqdn
                .Select(a => a.Trim('.'))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .OrderBy(a => a.Count(c => c == '.'))
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fqdn))
                continue;
            hosts.Add(fqdn);
        }

        NewSessionRemoteHost = null;
        AvailableHosts = hosts;
    }

    [RelayCommand]
    private void StartCreatingNewSession()
    {
        ClearNewForm();
        // Ensure we have a fresh hosts list before we open the form. We don't
        // bind directly to the list on RPC state updates as updating the list
        // while in use seems to break it.
        SetAvailableHostsFromRpcModel(_rpcController.GetState());
        CreatingNewSession = true;
    }

    [RelayCommand]
    public async Task OpenLocalPathSelectDialog()
    {
        if (!CanOpenLocalPath)
            return;

        if (LocalFolderPicker is null)
        {
            _windowService.ShowMessageWindow(
                "Local folder picker unavailable",
                "The UI did not provide a local folder picker implementation.",
                "Coder Connect");
            return;
        }

        NewSessionLocalPathDialogOpen = true;
        try
        {
            var path = await LocalFolderPicker();
            if (string.IsNullOrWhiteSpace(path))
                return;

            NewSessionLocalPath = path;
        }
        catch
        {
            // ignored
        }
        finally
        {
            NewSessionLocalPathDialogOpen = false;
        }
    }

    [RelayCommand]
    public void OpenRemotePathSelectDialog()
    {
        if (string.IsNullOrWhiteSpace(NewSessionRemoteHost))
            return;

        // The UI is responsible for rendering the picker when this ViewModel is set.
        if (RemotePathPickerViewModel is not null)
            return;

        NewSessionRemotePathDialogOpen = true;

        var pickerViewModel = new DirectoryPickerViewModel(_agentApiClientFactory, _dispatcher, _windowService, NewSessionRemoteHost);
        pickerViewModel.PathSelected += OnRemotePathSelected;
        pickerViewModel.CloseRequested += OnRemotePathPickerCloseRequested;

        RemotePathPickerViewModel = pickerViewModel;
        pickerViewModel.Initialize();
    }

    private void OnRemotePathSelected(object? sender, string? path)
    {
        if (path != null)
            NewSessionRemotePath = path;

        CloseRemotePathPicker(sender as DirectoryPickerViewModel);
    }

    private void OnRemotePathPickerCloseRequested(object? sender, EventArgs e)
    {
        CloseRemotePathPicker(sender as DirectoryPickerViewModel);
    }

    private void CloseRemotePathPicker(DirectoryPickerViewModel? pickerViewModel = null)
    {
        pickerViewModel ??= RemotePathPickerViewModel;
        if (pickerViewModel is not null)
        {
            pickerViewModel.PathSelected -= OnRemotePathSelected;
            pickerViewModel.CloseRequested -= OnRemotePathPickerCloseRequested;
        }

        RemotePathPickerViewModel = null;
        NewSessionRemotePathDialogOpen = false;
    }

    [RelayCommand]
    private void CancelNewSession()
    {
        ClearNewForm();
    }

    private void OnCreateSessionProgress(string message)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => OnCreateSessionProgress(message));
            return;
        }

        NewSessionStatus = message;
    }

    [RelayCommand]
    private async Task ConfirmNewSession()
    {
        if (OperationInProgress || !NewSessionCreateEnabled) return;
        OperationInProgress = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            // The controller will send us a state changed event.
            await _syncSessionController.CreateSyncSession(new CreateSyncSessionRequest
            {
                Alpha = new CreateSyncSessionRequest.Endpoint
                {
                    Protocol = CreateSyncSessionRequest.Endpoint.ProtocolKind.Local,
                    Path = NewSessionLocalPath,
                },
                Beta = new CreateSyncSessionRequest.Endpoint
                {
                    Protocol = CreateSyncSessionRequest.Endpoint.ProtocolKind.Ssh,
                    Host = NewSessionRemoteHost!,
                    Path = NewSessionRemotePath,
                },
            }, OnCreateSessionProgress, cts.Token);

            ClearNewForm();
        }
        catch (Exception e)
        {
            _windowService.ShowMessageWindow("Failed to create sync session", e.ToString(), "Coder Connect");
        }
        finally
        {
            OperationInProgress = false;
            NewSessionStatus = "";
        }
    }

    public async Task PauseOrResumeSession(string identifier)
    {
        if (OperationInProgress) return;
        OperationInProgress = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var actionString = "resume/pause";
        try
        {
            if (Sessions.FirstOrDefault(s => s.Model.Identifier == identifier) is not { } session)
                throw new InvalidOperationException("Session not found");

            // The controller will send us a state changed event.
            if (session.Model.Paused)
            {
                actionString = "resume";
                await _syncSessionController.ResumeSyncSession(session.Model.Identifier, cts.Token);
            }
            else
            {
                actionString = "pause";
                await _syncSessionController.PauseSyncSession(session.Model.Identifier, cts.Token);
            }
        }
        catch (Exception e)
        {
            _windowService.ShowMessageWindow(
                $"Failed to {actionString} sync session",
                $"Identifier: {identifier}\n{e}",
                "Coder Connect");
        }
        finally
        {
            OperationInProgress = false;
        }
    }

    public async Task TerminateSession(string identifier)
    {
        if (OperationInProgress) return;
        OperationInProgress = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            if (Sessions.FirstOrDefault(s => s.Model.Identifier == identifier) is not { } session)
                throw new InvalidOperationException("Session not found");

            var shouldTerminate = true;
            if (ConfirmTerminateSessionAsync is not null)
                shouldTerminate = await ConfirmTerminateSessionAsync(identifier);

            // TODO: Avalonia - add confirmation dialog in UI if needed.
            if (!shouldTerminate)
                return;

            // The controller will send us a state changed event.
            await _syncSessionController.TerminateSyncSession(session.Model.Identifier, cts.Token);
        }
        catch (Exception e)
        {
            _windowService.ShowMessageWindow(
                "Failed to terminate sync session",
                $"Identifier: {identifier}\n{e}",
                "Coder Connect");
        }
        finally
        {
            OperationInProgress = false;
        }
    }
}

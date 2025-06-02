using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Views;
using Coder.Desktop.CoderSdk.Agent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace Coder.Desktop.App.ViewModels;

public partial class FileSyncListViewModel : ObservableObject
{
    private Window? _window;
    private DispatcherQueue? _dispatcherQueue;
    private DirectoryPickerWindow? _remotePickerWindow;

    private readonly ISyncSessionController _syncSessionController;
    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;
    private readonly IAgentApiClientFactory _agentApiClientFactory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial string? UnavailableMessage { get; set; } = null;

    // Initially we use the current cached state, the loading screen is only
    // shown when the user clicks "Reload" on the error screen.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial bool Loading { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial string? Error { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenLocalPath))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemoteHostEnabled))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemotePathDialogEnabled))]
    public partial bool OperationInProgress { get; set; } = false;

    [ObservableProperty] public partial IReadOnlyList<SyncSessionViewModel> Sessions { get; set; } = [];

    [ObservableProperty] public partial bool CreatingNewSession { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial string NewSessionLocalPath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    [NotifyPropertyChangedFor(nameof(CanOpenLocalPath))]
    public partial bool NewSessionLocalPathDialogOpen { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionRemoteHostEnabled))]
    public partial IReadOnlyList<string> AvailableHosts { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemotePathDialogEnabled))]
    public partial string? NewSessionRemoteHost { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial string NewSessionRemotePath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    [NotifyPropertyChangedFor(nameof(NewSessionRemotePathDialogEnabled))]
    public partial bool NewSessionRemotePathDialogOpen { get; set; } = false;

    public bool CanOpenLocalPath => !NewSessionLocalPathDialogOpen && !OperationInProgress;

    public bool NewSessionRemoteHostEnabled => AvailableHosts.Count > 0 && !OperationInProgress;

    public bool NewSessionRemotePathDialogEnabled =>
        !string.IsNullOrWhiteSpace(NewSessionRemoteHost) && !NewSessionRemotePathDialogOpen && !OperationInProgress;

    [ObservableProperty] public partial string NewSessionStatus { get; set; } = "";

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
        ICredentialManager credentialManager, IAgentApiClientFactory agentApiClientFactory)
    {
        _syncSessionController = syncSessionController;
        _rpcController = rpcController;
        _credentialManager = credentialManager;
        _agentApiClientFactory = agentApiClientFactory;
    }

    public void Initialize(Window window, DispatcherQueue dispatcherQueue)
    {
        _window = window;
        _dispatcherQueue = dispatcherQueue;
        if (!_dispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Initialize must be called from the UI thread");

        _rpcController.StateChanged += RpcControllerStateChanged;
        _credentialManager.CredentialsChanged += CredentialManagerCredentialsChanged;
        _syncSessionController.StateChanged += SyncSessionStateChanged;
        _window.Closed += (_, _) =>
        {
            _remotePickerWindow?.Close();

            _rpcController.StateChanged -= RpcControllerStateChanged;
            _credentialManager.CredentialsChanged -= CredentialManagerCredentialsChanged;
            _syncSessionController.StateChanged -= SyncSessionStateChanged;
        };

        var rpcModel = _rpcController.GetState();
        var credentialModel = _credentialManager.GetCachedCredentials();
        var syncSessionState = _syncSessionController.GetState();
        UpdateSyncSessionState(syncSessionState);
        MaybeSetUnavailableMessage(rpcModel, credentialModel, syncSessionState);
    }

    private void RpcControllerStateChanged(object? sender, RpcModel rpcModel)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => RpcControllerStateChanged(sender, rpcModel));
            return;
        }

        var credentialModel = _credentialManager.GetCachedCredentials();
        var syncSessionState = _syncSessionController.GetState();
        MaybeSetUnavailableMessage(rpcModel, credentialModel, syncSessionState);
    }

    private void CredentialManagerCredentialsChanged(object? sender, CredentialModel credentialModel)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => CredentialManagerCredentialsChanged(sender, credentialModel));
            return;
        }

        var rpcModel = _rpcController.GetState();
        var syncSessionState = _syncSessionController.GetState();
        MaybeSetUnavailableMessage(rpcModel, credentialModel, syncSessionState);
    }

    private void SyncSessionStateChanged(object? sender, SyncSessionControllerStateModel syncSessionState)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => SyncSessionStateChanged(sender, syncSessionState));
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
        _remotePickerWindow?.Close();
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
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => HandleRefresh(t));
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
                .Aggregate((a, b) => a.Count(c => c == '.') < b.Count(c => c == '.') ? a : b);
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
        if (_window is null) return;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        var hwnd = WindowNative.GetWindowHandle(_window);
        InitializeWithWindow.Initialize(picker, hwnd);

        NewSessionLocalPathDialogOpen = true;
        try
        {
            var path = await picker.PickSingleFolderAsync();
            if (path == null) return;
            NewSessionLocalPath = path.Path;
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
        if (_remotePickerWindow is not null)
        {
            _remotePickerWindow.Activate();
            return;
        }

        NewSessionRemotePathDialogOpen = true;
        var pickerViewModel = new DirectoryPickerViewModel(_agentApiClientFactory, NewSessionRemoteHost);
        pickerViewModel.PathSelected += OnRemotePathSelected;

        _remotePickerWindow = new DirectoryPickerWindow(pickerViewModel);
        if (_window is not null)
            _remotePickerWindow.SetParent(_window);
        _remotePickerWindow.Closed += (_, _) =>
        {
            _remotePickerWindow = null;
            NewSessionRemotePathDialogOpen = false;
        };
        _remotePickerWindow.Activate();
    }

    private void OnRemotePathSelected(object? sender, string? path)
    {
        if (sender is not DirectoryPickerViewModel pickerViewModel) return;
        pickerViewModel.PathSelected -= OnRemotePathSelected;

        if (path == null) return;
        NewSessionRemotePath = path;
    }

    [RelayCommand]
    private void CancelNewSession()
    {
        ClearNewForm();
    }

    private void OnCreateSessionProgress(string message)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => OnCreateSessionProgress(message));
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
            var dialog = new ContentDialog
            {
                Title = "Failed to create sync session",
                Content = $"{e}",
                CloseButtonText = "Ok",
                XamlRoot = _window?.Content.XamlRoot,
            };
            _ = await dialog.ShowAsync();
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
            var dialog = new ContentDialog
            {
                Title = $"Failed to {actionString} sync session",
                Content = $"Identifier: {identifier}\n{e}",
                CloseButtonText = "Ok",
                XamlRoot = _window?.Content.XamlRoot,
            };
            _ = await dialog.ShowAsync();
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

            var confirmDialog = new ContentDialog
            {
                Title = "Terminate sync session",
                Content = "Are you sure you want to terminate this sync session?",
                PrimaryButtonText = "Terminate",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _window?.Content.XamlRoot,
            };
            var res = await confirmDialog.ShowAsync();
            if (res is not ContentDialogResult.Primary)
                return;

            // The controller will send us a state changed event.
            await _syncSessionController.TerminateSyncSession(session.Model.Identifier, cts.Token);
        }
        catch (Exception e)
        {
            var dialog = new ContentDialog
            {
                Title = "Failed to terminate sync session",
                Content = $"Identifier: {identifier}\n{e}",
                CloseButtonText = "Ok",
                XamlRoot = _window?.Content.XamlRoot,
            };
            _ = await dialog.ShowAsync();
        }
        finally
        {
            OperationInProgress = false;
        }
    }
}

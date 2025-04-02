using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
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

    private readonly ISyncSessionController _syncSessionController;
    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial string? UnavailableMessage { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial bool Loading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial string? Error { get; set; } = null;

    [ObservableProperty] public partial bool OperationInProgress { get; set; } = false;

    [ObservableProperty] public partial List<SyncSessionViewModel> Sessions { get; set; } = [];

    [ObservableProperty] public partial bool CreatingNewSession { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial string NewSessionLocalPath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial bool NewSessionLocalPathDialogOpen { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial string NewSessionRemoteHost { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial string NewSessionRemotePath { get; set; } = "";
    // TODO: NewSessionRemotePathDialogOpen for remote path

    public bool NewSessionCreateEnabled
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NewSessionLocalPath)) return false;
            if (NewSessionLocalPathDialogOpen) return false;
            if (string.IsNullOrWhiteSpace(NewSessionRemoteHost)) return false;
            if (string.IsNullOrWhiteSpace(NewSessionRemotePath)) return false;
            return true;
        }
    }

    // TODO: this could definitely be improved
    public bool ShowUnavailable => UnavailableMessage != null;
    public bool ShowLoading => Loading && UnavailableMessage == null && Error == null;
    public bool ShowError => UnavailableMessage == null && Error != null;
    public bool ShowSessions => !Loading && UnavailableMessage == null && Error == null;

    public FileSyncListViewModel(ISyncSessionController syncSessionController, IRpcController rpcController,
        ICredentialManager credentialManager)
    {
        _syncSessionController = syncSessionController;
        _rpcController = rpcController;
        _credentialManager = credentialManager;
    }

    public void Initialize(Window window, DispatcherQueue dispatcherQueue)
    {
        _window = window;
        _dispatcherQueue = dispatcherQueue;
        if (!_dispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Initialize must be called from the UI thread");

        _rpcController.StateChanged += RpcControllerStateChanged;
        _credentialManager.CredentialsChanged += CredentialManagerCredentialsChanged;

        var rpcModel = _rpcController.GetState();
        var credentialModel = _credentialManager.GetCachedCredentials();
        MaybeSetUnavailableMessage(rpcModel, credentialModel);
        if (UnavailableMessage == null) ReloadSessions();
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
        MaybeSetUnavailableMessage(rpcModel, credentialModel);
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
        MaybeSetUnavailableMessage(rpcModel, credentialModel);
    }

    private void MaybeSetUnavailableMessage(RpcModel rpcModel, CredentialModel credentialModel)
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
        else
        {
            UnavailableMessage = null;
            if (oldMessage != null) ReloadSessions();
        }
    }

    private void ClearNewForm()
    {
        CreatingNewSession = false;
        NewSessionLocalPath = "";
        NewSessionRemoteHost = "";
        NewSessionRemotePath = "";
    }

    [RelayCommand]
    private void ReloadSessions()
    {
        Loading = true;
        Error = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _syncSessionController.ListSyncSessions(cts.Token).ContinueWith(HandleList, CancellationToken.None);
    }

    private void HandleList(Task<IEnumerable<SyncSessionModel>> t)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => HandleList(t));
            return;
        }

        if (t.IsCompletedSuccessfully)
        {
            Sessions = t.Result.Select(s => new SyncSessionViewModel(this, s)).ToList();
            Loading = false;
            return;
        }

        Error = "Could not list sync sessions: ";
        if (t.IsCanceled) Error += new TaskCanceledException();
        else if (t.IsFaulted) Error += t.Exception;
        else Error += "no successful result or error";
        Loading = false;
    }

    [RelayCommand]
    private void StartCreatingNewSession()
    {
        ClearNewForm();
        CreatingNewSession = true;
    }

    public async Task OpenLocalPathSelectDialog(Window window)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        var hwnd = WindowNative.GetWindowHandle(window);
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
    private void CancelNewSession()
    {
        ClearNewForm();
    }

    [RelayCommand]
    private async Task ConfirmNewSession()
    {
        if (OperationInProgress || !NewSessionCreateEnabled) return;
        OperationInProgress = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await _syncSessionController.CreateSyncSession(new CreateSyncSessionRequest
            {
                Alpha = new CreateSyncSessionRequestEndpoint
                {
                    Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                    Path = NewSessionLocalPath,
                },
                Beta = new CreateSyncSessionRequestEndpoint
                {
                    Protocol = CreateSyncSessionRequestEndpointProtocol.Ssh,
                    Host = NewSessionRemoteHost,
                    Path = NewSessionRemotePath,
                },
            }, cts.Token);

            ClearNewForm();
            ReloadSessions();
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

            ReloadSessions();
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

            await _syncSessionController.TerminateSyncSession(session.Model.Identifier, cts.Token);

            ReloadSessions();
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

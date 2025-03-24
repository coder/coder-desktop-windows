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
using WinRT.Interop;

namespace Coder.Desktop.App.ViewModels;

public partial class FileSyncListViewModel : ObservableObject
{
    public delegate void OnFileSyncListStaleDelegate();

    // Triggered when the window should be closed.
    public event OnFileSyncListStaleDelegate? OnFileSyncListStale;

    private DispatcherQueue? _dispatcherQueue;

    private readonly ISyncSessionController _syncSessionController;
    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial bool Loading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    public partial string? Error { get; set; } = null;

    [ObservableProperty] public partial List<SyncSessionModel> Sessions { get; set; } = [];

    [ObservableProperty] public partial bool CreatingNewSession { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial string NewSessionLocalPath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial bool NewSessionLocalPathDialogOpen { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSessionCreateEnabled))]
    public partial string NewSessionRemoteName { get; set; } = "";

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
            if (string.IsNullOrWhiteSpace(NewSessionRemoteName)) return false;
            if (string.IsNullOrWhiteSpace(NewSessionRemotePath)) return false;
            return true;
        }
    }

    public bool ShowLoading => Loading && Error == null;
    public bool ShowError => Error != null;
    public bool ShowSessions => !Loading && Error == null;

    public FileSyncListViewModel(ISyncSessionController syncSessionController, IRpcController rpcController,
        ICredentialManager credentialManager)
    {
        _syncSessionController = syncSessionController;
        _rpcController = rpcController;
        _credentialManager = credentialManager;

        Sessions =
        [
            new SyncSessionModel(@"C:\Users\dean\git\coder-desktop-windows", "pog", "~/repos/coder-desktop-windows",
                SyncSessionStatusCategory.Ok, "Watching", "Some description", []),
            new SyncSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", SyncSessionStatusCategory.Paused,
                "Paused",
                "Some description", []),
            new SyncSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", SyncSessionStatusCategory.Conflicts,
                "Conflicts", "Some description", []),
            new SyncSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", SyncSessionStatusCategory.Error,
                "Halted on root emptied", "Some description", []),
            new SyncSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", SyncSessionStatusCategory.Unknown,
                "Unknown", "Some description", []),
            new SyncSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", SyncSessionStatusCategory.Working,
                "Reconciling", "Some description", []),
        ];
    }

    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        _rpcController.StateChanged += (_, rpcModel) => UpdateFromRpcModel(rpcModel);
        _credentialManager.CredentialsChanged += (_, credentialModel) => UpdateFromCredentialsModel(credentialModel);

        var rpcModel = _rpcController.GetState();
        var credentialModel = _credentialManager.GetCachedCredentials();
        // TODO: fix this
        //if (MaybeSendStaleEvent(rpcModel, credentialModel)) return;

        // TODO: Simulate loading until we have real data.
        Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => _dispatcherQueue.TryEnqueue(() => Loading = false));
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

        var credentialModel = _credentialManager.GetCachedCredentials();
        MaybeSendStaleEvent(rpcModel, credentialModel);
    }

    private void UpdateFromCredentialsModel(CredentialModel credentialModel)
    {
        // Ensure we're on the UI thread.
        if (_dispatcherQueue == null) return;
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateFromCredentialsModel(credentialModel));
            return;
        }

        var rpcModel = _rpcController.GetState();
        MaybeSendStaleEvent(rpcModel, credentialModel);
    }

    private bool MaybeSendStaleEvent(RpcModel rpcModel, CredentialModel credentialModel)
    {
        var ok = rpcModel.RpcLifecycle is RpcLifecycle.Connected
                 && rpcModel.VpnLifecycle is VpnLifecycle.Started
                 && credentialModel.State == CredentialState.Valid;

        if (!ok) OnFileSyncListStale?.Invoke();
        return !ok;
    }

    private void ClearNewForm()
    {
        CreatingNewSession = false;
        NewSessionLocalPath = "";
        NewSessionRemoteName = "";
        NewSessionRemotePath = "";
    }

    [RelayCommand]
    private void ReloadSessions()
    {
        Loading = true;
        Error = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _ = _syncSessionController.ListSyncSessions(cts.Token).ContinueWith(HandleList, cts.Token);
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
            Sessions = t.Result.ToList();
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
    private void ConfirmNewSession()
    {
        // TODO: implement
        ClearNewForm();
    }
}

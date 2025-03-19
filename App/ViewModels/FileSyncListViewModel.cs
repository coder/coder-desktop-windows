using System;
using System.Collections.Generic;
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

    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;

    [ObservableProperty] public partial List<MutagenSessionModel> Sessions { get; set; } = [];

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

    public FileSyncListViewModel(IRpcController rpcController, ICredentialManager credentialManager)
    {
        _rpcController = rpcController;
        _credentialManager = credentialManager;

        Sessions =
        [
            new MutagenSessionModel(@"C:\Users\dean\git\coder-desktop-windows", "pog", "~/repos/coder-desktop-windows",
                MutagenSessionStatus.Ok, "Watching", "Some description", []),
            new MutagenSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", MutagenSessionStatus.Paused, "Paused",
                "Some description", []),
            new MutagenSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", MutagenSessionStatus.NeedsAttention,
                "Conflicts", "Some description", []),
            new MutagenSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", MutagenSessionStatus.Error,
                "Halted on root emptied", "Some description", []),
            new MutagenSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", MutagenSessionStatus.Unknown,
                "Unknown", "Some description", []),
            new MutagenSessionModel(@"C:\Users\dean\git\coder", "pog", "~/coder", MutagenSessionStatus.Working,
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
        MaybeSendStaleEvent(rpcModel, credentialModel);
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

    private void MaybeSendStaleEvent(RpcModel rpcModel, CredentialModel credentialModel)
    {
        var ok = rpcModel.RpcLifecycle is RpcLifecycle.Connected
                 && rpcModel.VpnLifecycle is VpnLifecycle.Started
                 && credentialModel.State == CredentialState.Valid;

        if (!ok) OnFileSyncListStale?.Invoke();
    }

    private void ClearNewForm()
    {
        CreatingNewSession = false;
        NewSessionLocalPath = "";
        // TODO: close the dialog somehow
        NewSessionRemoteName = "";
        NewSessionRemotePath = "";
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
            // TODO: Needed?
            //FileTypeFilter = { "*" },
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

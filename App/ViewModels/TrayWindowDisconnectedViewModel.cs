using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Views.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowDisconnectedViewModel : ObservableObject
{
    private readonly IRpcController _rpcController;

    [ObservableProperty] public partial bool ReconnectButtonEnabled { get; set; } = true;
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ReconnectFailed { get; set; } = false;

    public TrayWindowDisconnectedViewModel(IRpcController rpcController)
    {
        _rpcController = rpcController;
        _rpcController.StateChanged += (_, rpcModel) => UpdateFromRpcModel(rpcModel);
    }

    private void UpdateFromRpcModel(RpcModel rpcModel)
    {
        ReconnectButtonEnabled = rpcModel.RpcLifecycle != RpcLifecycle.Disconnected;
    }

    [RelayCommand]
    public async Task Reconnect()
    {
        try
        {
            ReconnectFailed = false;
            ErrorMessage = string.Empty;
            await _rpcController.Reconnect();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ReconnectFailed = true;
        }
    }
}

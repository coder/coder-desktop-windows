using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowDisconnectedViewModel : ObservableObject
{
    private readonly IRpcController _rpcController;

    [ObservableProperty]
    private bool _reconnectButtonEnabled = true;
    [ObservableProperty]
    private string _errorMessage = string.Empty;
    [ObservableProperty]
    private bool _reconnectFailed = false;

    public TrayWindowDisconnectedViewModel(IRpcController rpcController)
    {
        _rpcController = rpcController;
        _rpcController.StateChanged += (_, rpcModel) => UpdateFromRpcModel(rpcModel);
    }

    private void UpdateFromRpcModel(RpcModel rpcModel)
    {
        ReconnectButtonEnabled = rpcModel.RpcLifecycle == RpcLifecycle.Disconnected;
    }

    [RelayCommand]
    public async Task Reconnect()
    {
        try
        {
            ReconnectFailed = false;
            ErrorMessage = string.Empty;
            ReconnectButtonEnabled = false;
            await _rpcController.Reconnect();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ReconnectFailed = true;
            ReconnectButtonEnabled = true;
        }
    }
}

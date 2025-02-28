using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowDisconnectedViewModel : ObservableObject
{
    private readonly IRpcController _rpcController;

    [ObservableProperty] public partial bool ReconnectButtonEnabled { get; set; } = true;

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
        await _rpcController.Reconnect();
    }
}

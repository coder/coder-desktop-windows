using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowLoginRequiredViewModel
{
    private readonly IWindowService _windowService;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public TrayWindowLoginRequiredViewModel(IWindowService windowService, IHostApplicationLifetime applicationLifetime)
    {
        _windowService = windowService;
        _applicationLifetime = applicationLifetime;
    }

    [RelayCommand]
    public void Login()
    {
        _windowService.ShowSignInWindow();
    }

    [RelayCommand]
    public void Exit()
    {
        _applicationLifetime.StopApplication();
    }
}

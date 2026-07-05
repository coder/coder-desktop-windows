using System;
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
        try
        {
            _windowService.ShowSignInWindow();
        }
        catch (Exception ex)
        {
            _windowService.ShowMessageWindow("Failed to open sign-in", ex.ToString(), "Coder Connect");
        }
    }

    [RelayCommand]
    public void Exit()
    {
        _applicationLifetime.StopApplication();
    }
}

using System;
using Coder.Desktop.App.Views;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowLoginRequiredViewModel
{
    private readonly IServiceProvider _services;

    private SignInWindow? _signInWindow;

    public TrayWindowLoginRequiredViewModel(IServiceProvider services)
    {
        _services = services;
    }

    [RelayCommand]
    public void Login()
    {
        // This is safe against concurrent access since it all happens in the
        // UI thread.
        if (_signInWindow != null)
        {
            _signInWindow.Activate();
            return;
        }

        _signInWindow = _services.GetRequiredService<SignInWindow>();
        _signInWindow.Closed += (_, _) => _signInWindow = null;
        _signInWindow.Activate();
    }
}

using System;
using System.Diagnostics;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Coder.Desktop.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICredentialManager, CredentialManager>();
        services.AddSingleton<IRpcController, RpcController>();

        // SignInWindow views and view models
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SignInWindow>();

        // TrayWindow views and view models
        services.AddTransient<TrayWindowDisconnectedViewModel>();
        services.AddTransient<TrayWindowDisconnectedPage>();
        services.AddTransient<TrayWindowLoginRequiredViewModel>();
        services.AddTransient<TrayWindowLoginRequiredPage>();
        services.AddTransient<TrayWindowLoginRequiredViewModel>();
        services.AddTransient<TrayWindowLoginRequiredPage>();
        services.AddTransient<TrayWindowViewModel>();
        services.AddTransient<TrayWindowMainPage>();
        services.AddTransient<TrayWindow>();

        _services = services.BuildServiceProvider();

#if DEBUG
        UnhandledException += (_, e) => { Debug.WriteLine(e.Exception.ToString()); };
#endif

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var trayWindow = _services.GetRequiredService<TrayWindow>();
        trayWindow.Closed += (sender, args) =>
        {
            args.Handled = true;
            trayWindow.AppWindow.Hide();
        };
    }
}

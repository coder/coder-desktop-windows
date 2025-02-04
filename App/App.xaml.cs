using System;
using System.Diagnostics;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Views;
using Coder.Desktop.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private TrayWindow? _trayWindow;
    private readonly bool _handleClosedEvents = true;

    public App()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICredentialManager, CredentialManager>();
        services.AddSingleton<IRpcController, RpcController>();

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
        _trayWindow = _services.GetRequiredService<TrayWindow>();
        _trayWindow.Closed += (sender, args) =>
        {
            // TODO: wire up HandleClosedEvents properly
            if (_handleClosedEvents)
            {
                args.Handled = true;
                _trayWindow.AppWindow.Hide();
            }
        };
    }
}

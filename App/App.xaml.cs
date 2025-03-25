using System;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Coder.Desktop.App.Views.Pages;
using Coder.Desktop.Vpn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    private bool _handleWindowClosed = true;

#if !DEBUG
    private const string MutagenControllerConfigSection = "AppMutagenController";
#else
    private const string MutagenControllerConfigSection = "DebugAppMutagenController";
#endif

    public App()
    {
        var builder = Host.CreateApplicationBuilder();

        (builder.Configuration as IConfigurationBuilder).Add(
            new RegistryConfigurationSource(Registry.LocalMachine, @"SOFTWARE\Coder Desktop"));

        var services = builder.Services;

        services.AddSingleton<ICredentialManager, CredentialManager>();
        services.AddSingleton<IRpcController, RpcController>();

        services.AddOptions<MutagenControllerConfig>()
            .Bind(builder.Configuration.GetSection(MutagenControllerConfigSection));
        services.AddSingleton<ISyncSessionController, MutagenController>();

        // SignInWindow views and view models
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SignInWindow>();

        // FileSyncListWindow views and view models
        services.AddTransient<FileSyncListViewModel>();
        // FileSyncListMainPage is created by FileSyncListWindow.
        services.AddTransient<FileSyncListWindow>();

        // TrayWindow views and view models
        services.AddTransient<TrayWindowLoadingPage>();
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

        InitializeComponent();
    }

    public async Task ExitApplication()
    {
        _handleWindowClosed = false;
        Exit();
        var rpcController = _services.GetRequiredService<IRpcController>();
        // TODO: send a StopRequest if we're connected???
        await rpcController.DisposeAsync();
        Environment.Exit(0);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Start connecting to the manager in the background.
        var rpcController = _services.GetRequiredService<IRpcController>();
        if (rpcController.GetState().RpcLifecycle == RpcLifecycle.Disconnected)
            // Passing in a CT with no cancellation is desired here, because
            // the named pipe open will block until the pipe comes up.
            _ = rpcController.Reconnect(CancellationToken.None);

        // Load the credentials in the background. Even though we pass a CT
        // with no cancellation, the method itself will impose a timeout on the
        // HTTP portion.
        var credentialManager = _services.GetRequiredService<ICredentialManager>();
        _ = credentialManager.LoadCredentials(CancellationToken.None);

        // Prevent the TrayWindow from closing, just hide it.
        var trayWindow = _services.GetRequiredService<TrayWindow>();
        trayWindow.Closed += (sender, args) =>
        {
            if (!_handleWindowClosed) return;
            args.Handled = true;
            trayWindow.AppWindow.Hide();
        };
    }
}

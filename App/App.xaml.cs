using System;
using System.Diagnostics;
using System.IO;
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
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

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
        var syncController = _services.GetRequiredService<ISyncSessionController>();
        await syncController.DisposeAsync();
        var rpcController = _services.GetRequiredService<IRpcController>();
        // TODO: send a StopRequest if we're connected???
        await rpcController.DisposeAsync();
        Environment.Exit(0);
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Start connecting to the manager in the background.
        var rpcController = _services.GetRequiredService<IRpcController>();
        if (rpcController.GetState().RpcLifecycle == RpcLifecycle.Disconnected)
            // Passing in a CT with no cancellation is desired here, because
            // the named pipe open will block until the pipe comes up.
            // TODO: log
            _ = rpcController.Reconnect(CancellationToken.None).ContinueWith(t =>
            {
#if DEBUG
                if (t.Exception != null)
                {
                    Debug.WriteLine(t.Exception);
                    Debugger.Break();
                }
#endif
            });

        // Load the credentials in the background.
        var credentialManagerCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var credentialManager = _services.GetRequiredService<ICredentialManager>();
        _ = credentialManager.LoadCredentials(credentialManagerCts.Token).ContinueWith(t =>
        {
            // TODO: log
#if DEBUG
            if (t.Exception != null)
            {
                Debug.WriteLine(t.Exception);
                Debugger.Break();
            }
#endif
            credentialManagerCts.Dispose();
        }, CancellationToken.None);

        // Initialize file sync.
        var syncSessionCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var syncSessionController = _services.GetRequiredService<ISyncSessionController>();
        _ = syncSessionController.RefreshState(syncSessionCts.Token).ContinueWith(t =>
        {
            // TODO: log
#if DEBUG
            if (t.IsCanceled || t.Exception != null) Debugger.Break();
#endif
            syncSessionCts.Dispose();
        }, CancellationToken.None);

        // Prevent the TrayWindow from closing, just hide it.
        var trayWindow = _services.GetRequiredService<TrayWindow>();
        trayWindow.Closed += (_, closedArgs) =>
        {
            if (!_handleWindowClosed) return;
            closedArgs.Handled = true;
            trayWindow.AppWindow.Hide();
        };
    }

    public void OnActivated(object? sender, AppActivationArguments args)
    {
        switch (args.Kind)
        {
            case ExtendedActivationKind.Protocol:
                var protoArgs = args.Data as IProtocolActivatedEventArgs;
                HandleURIActivation(protoArgs.Uri);
                break;

            default:
                // TODO: log
                break;
        }
    }

    public void HandleURIActivation(Uri uri)
    {
        // TODO: handle
    }
}

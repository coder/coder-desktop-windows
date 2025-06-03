using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Coder.Desktop.App.Views.Pages;
using Coder.Desktop.CoderSdk.Agent;
using Coder.Desktop.CoderSdk.Coder;
using Coder.Desktop.Vpn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Serilog;
using LaunchActivatedEventArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    private bool _handleWindowClosed = true;
    private const string MutagenControllerConfigSection = "MutagenController";

#if !DEBUG
    private const string ConfigSubKey = @"SOFTWARE\Coder Desktop\App";
    private const string logFilename = "app.log";
#else
    private const string ConfigSubKey = @"SOFTWARE\Coder Desktop\DebugApp";
    private const string logFilename = "debug-app.log";
#endif

    private readonly ILogger<App> _logger;
    private readonly IUriHandler _uriHandler;

    private readonly ISettingsManager _settingsManager;

    private readonly IHostApplicationLifetime _appLifetime;

    public App()
    {
        var builder = Host.CreateApplicationBuilder();
        var configBuilder = builder.Configuration as IConfigurationBuilder;

        // Add config in increasing order of precedence: first builtin defaults, then HKLM, finally HKCU
        // so that the user's settings in the registry take precedence.
        AddDefaultConfig(configBuilder);
        configBuilder.Add(
            new RegistryConfigurationSource(Registry.LocalMachine, ConfigSubKey));
        configBuilder.Add(
            new RegistryConfigurationSource(Registry.CurrentUser, ConfigSubKey));

        var services = builder.Services;

        // Logging
        builder.Services.AddSerilog((_, loggerConfig) =>
        {
            loggerConfig.ReadFrom.Configuration(builder.Configuration);
        });

        services.AddSingleton<ICoderApiClientFactory, CoderApiClientFactory>();
        services.AddSingleton<IAgentApiClientFactory, AgentApiClientFactory>();

        services.AddSingleton<ICredentialBackend>(_ =>
            new WindowsCredentialBackend(WindowsCredentialBackend.CoderCredentialsTargetName));
        services.AddSingleton<ICredentialManager, CredentialManager>();
        services.AddSingleton<IRpcController, RpcController>();
        services.AddSingleton<IHostnameSuffixGetter, HostnameSuffixGetter>();

        services.AddOptions<MutagenControllerConfig>()
            .Bind(builder.Configuration.GetSection(MutagenControllerConfigSection));
        services.AddSingleton<ISyncSessionController, MutagenController>();
        services.AddSingleton<IUserNotifier, UserNotifier>();
        services.AddSingleton<IRdpConnector, RdpConnector>();
        services.AddSingleton<IUriHandler, UriHandler>();

        // SignInWindow views and view models
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SignInWindow>();

        // FileSyncListWindow views and view models
        services.AddTransient<FileSyncListViewModel>();
        // FileSyncListMainPage is created by FileSyncListWindow.
        services.AddTransient<FileSyncListWindow>();

        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<IStartupManager, StartupManager>();
        // SettingsWindow views and view models
        services.AddTransient<SettingsViewModel>();
        // SettingsMainPage is created by SettingsWindow.
        services.AddTransient<SettingsWindow>();

        // DirectoryPickerWindow views and view models are created by FileSyncListViewModel.

        // TrayWindow views and view models
        services.AddTransient<TrayWindowLoadingPage>();
        services.AddTransient<TrayWindowDisconnectedViewModel>();
        services.AddTransient<TrayWindowDisconnectedPage>();
        services.AddTransient<TrayWindowLoginRequiredViewModel>();
        services.AddTransient<TrayWindowLoginRequiredPage>();
        services.AddTransient<TrayWindowLoginRequiredViewModel>();
        services.AddTransient<TrayWindowLoginRequiredPage>();
        services.AddSingleton<IAgentAppViewModelFactory, AgentAppViewModelFactory>();
        services.AddSingleton<IAgentViewModelFactory, AgentViewModelFactory>();
        services.AddTransient<TrayWindowViewModel>();
        services.AddTransient<TrayWindowMainPage>();
        services.AddTransient<TrayWindow>();

        _services = services.BuildServiceProvider();
        _logger = (ILogger<App>)_services.GetService(typeof(ILogger<App>))!;
        _uriHandler = (IUriHandler)_services.GetService(typeof(IUriHandler))!;
        _settingsManager = (ISettingsManager)_services.GetService(typeof(ISettingsManager))!;
        _appLifetime = (IHostApplicationLifetime)_services.GetRequiredService<IHostApplicationLifetime>();

        InitializeComponent();
    }

    public async Task ExitApplication()
    {
        _logger.LogDebug("exiting app");
        _handleWindowClosed = false;
        Exit();
        var syncController = _services.GetRequiredService<ISyncSessionController>();
        await syncController.DisposeAsync();
        var rpcController = _services.GetRequiredService<IRpcController>();
        // TODO: send a StopRequest if we're connected???
        await rpcController.DisposeAsync();
        Environment.Exit(0);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInformation("new instance launched");

        _ = InitializeServicesAsync(_appLifetime.ApplicationStopping);

        // Prevent the TrayWindow from closing, just hide it.
        var trayWindow = _services.GetRequiredService<TrayWindow>();
        trayWindow.Closed += (_, closedArgs) =>
        {
            if (!_handleWindowClosed) return;
            closedArgs.Handled = true;
            trayWindow.AppWindow.Hide();
        };
    }

    /// <summary>
    /// Loads stored VPN credentials, reconnects the RPC controller,
    /// and (optionally) starts the VPN tunnel on application launch.
    /// </summary>
    private async Task InitializeServicesAsync(CancellationToken cancellationToken = default)
    {
        var credentialManager = _services.GetRequiredService<ICredentialManager>();
        var rpcController = _services.GetRequiredService<IRpcController>();

        using var credsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        credsCts.CancelAfter(TimeSpan.FromSeconds(15));

        Task loadCredsTask = credentialManager.LoadCredentials(credsCts.Token);
        Task reconnectTask = rpcController.Reconnect(cancellationToken);

        try
        {
            await Task.WhenAll(loadCredsTask, reconnectTask);
        }
        catch (Exception)
        {
            if (loadCredsTask.IsFaulted)
                _logger.LogError(loadCredsTask.Exception!.GetBaseException(),
                                 "Failed to load credentials");

            if (reconnectTask.IsFaulted)
                _logger.LogError(reconnectTask.Exception!.GetBaseException(),
                                 "Failed to connect to VPN service");

            return;
        }

        if (_settingsManager.ConnectOnLaunch)
        {
            try
            {
                await rpcController.StartVpn(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect on launch");
            }
        }

        // Initialize file sync.
        var syncSessionCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var syncSessionController = _services.GetRequiredService<ISyncSessionController>();
        try
        {
            await syncSessionController.RefreshState(syncSessionCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to refresh sync session state {ex.Message}", ex);
        }
    }

    public void OnActivated(object? sender, AppActivationArguments args)
    {
        switch (args.Kind)
        {
            case ExtendedActivationKind.Protocol:
                var protoArgs = args.Data as IProtocolActivatedEventArgs;
                if (protoArgs == null)
                {
                    _logger.LogWarning("URI activation with null data");
                    return;
                }

                // don't need to wait for it to complete.
                _uriHandler.HandleUri(protoArgs.Uri).ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        // don't log query params, as they contain secrets.
                        _logger.LogError(t.Exception,
                            "unhandled exception while processing URI coder://{authority}{path}",
                            protoArgs.Uri.Authority, protoArgs.Uri.AbsolutePath);
                    }
                });

                break;

            case ExtendedActivationKind.AppNotification:
                var notificationArgs = (args.Data as AppNotificationActivatedEventArgs)!;
                HandleNotification(null, notificationArgs);
                break;

            default:
                _logger.LogWarning("activation for {kind}, which is unhandled", args.Kind);
                break;
        }
    }

    public void HandleNotification(AppNotificationManager? sender, AppNotificationActivatedEventArgs args)
    {
        // right now, we don't do anything other than log
        _logger.LogInformation("handled notification activation");
    }

    private static void AddDefaultConfig(IConfigurationBuilder builder)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoderDesktop",
            logFilename);
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [MutagenControllerConfigSection + ":MutagenExecutablePath"] = @"C:\mutagen.exe",
            ["Serilog:Using:0"] = "Serilog.Sinks.File",
            ["Serilog:MinimumLevel"] = "Information",
            ["Serilog:Enrich:0"] = "FromLogContext",
            ["Serilog:WriteTo:0:Name"] = "File",
            ["Serilog:WriteTo:0:Args:path"] = logPath,
            ["Serilog:WriteTo:0:Args:outputTemplate"] =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
            ["Serilog:WriteTo:0:Args:rollingInterval"] = "Day",
        });
    }
}

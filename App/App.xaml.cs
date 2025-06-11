using System;
using System.Collections.Generic;
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
using NetSparkleUpdater.Interfaces;
using Serilog;
using LaunchActivatedEventArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private const string MutagenControllerConfigSection = "MutagenController";
    private const string UpdaterConfigSection = "Updater";

#if !DEBUG
    private const string ConfigSubKey = @"SOFTWARE\Coder Desktop\App";
    private const string LogFilename = "app.log";
    private const string DefaultLogLevel = "Information";
#else
    private const string ConfigSubKey = @"SOFTWARE\Coder Desktop\DebugApp";
    private const string LogFilename = "debug-app.log";
    private const string DefaultLogLevel = "Debug";
#endif

    // HACK: This is exposed for dispatcher queue access. The notifier uses
    //       this to ensure action callbacks run in the UI thread (as
    //       activation events aren't in the main thread).
    public TrayWindow? TrayWindow;

    private readonly IServiceProvider _services;
    private readonly ILogger<App> _logger;
    private readonly IUriHandler _uriHandler;
    private readonly IUserNotifier _userNotifier;

    private bool _handleWindowClosed = true;

    private readonly ISettingsManager<CoderConnectSettings> _settingsManager;

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
            new RegistryConfigurationSource(
                Registry.CurrentUser,
                ConfigSubKey,
                // Block "Updater:" configuration from HKCU, so that updater
                // settings can only be set at the HKLM level.
                //
                // HACK: This isn't super robust, but the security risk is
                //       minor anyway. Malicious apps running as the user could
                //       likely override this setting by altering the memory of
                //       this app.
                UpdaterConfigSection + ":"));

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

        services.AddOptions<UpdaterConfig>()
            .Bind(builder.Configuration.GetSection(UpdaterConfigSection));
        services.AddSingleton<IUpdaterUpdateAvailableViewModelFactory, UpdaterUpdateAvailableViewModelFactory>();
        services.AddSingleton<IUIFactory, CoderSparkleUIFactory>();
        services.AddSingleton<IUpdateController, SparkleUpdateController>();

        // SignInWindow views and view models
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SignInWindow>();

        // FileSyncListWindow views and view models
        services.AddTransient<FileSyncListViewModel>();
        // FileSyncListMainPage is created by FileSyncListWindow.
        services.AddTransient<FileSyncListWindow>();

        services.AddSingleton<ISettingsManager<CoderConnectSettings>, SettingsManager<CoderConnectSettings>>();
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
        _logger = _services.GetRequiredService<ILogger<App>>();
        _uriHandler = _services.GetRequiredService<IUriHandler>();
        _userNotifier = _services.GetRequiredService<IUserNotifier>();
        _settingsManager = _services.GetRequiredService<ISettingsManager<CoderConnectSettings>>();
        _appLifetime = _services.GetRequiredService<IHostApplicationLifetime>();

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

        // Prevent the TrayWindow from closing, just hide it.
        if (TrayWindow != null)
            throw new InvalidOperationException("OnLaunched was called multiple times? TrayWindow is already set");
        TrayWindow = _services.GetRequiredService<TrayWindow>();
        TrayWindow.Closed += (_, closedArgs) =>
        {
            if (!_handleWindowClosed) return;
            closedArgs.Handled = true;
            TrayWindow.AppWindow.Hide();
        };

        _ = InitializeServicesAsync(_appLifetime.ApplicationStopping);
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

        var loadCredsTask = credentialManager.LoadCredentials(credsCts.Token);
        var reconnectTask = rpcController.Reconnect(cancellationToken);
        var settingsTask = _settingsManager.Read(cancellationToken);

        var dependenciesLoaded = true;

        try
        {
            await Task.WhenAll(loadCredsTask, reconnectTask, settingsTask);
        }
        catch (Exception)
        {
            if (loadCredsTask.IsFaulted)
                _logger.LogError(loadCredsTask.Exception!.GetBaseException(),
                                 "Failed to load credentials");

            if (reconnectTask.IsFaulted)
                _logger.LogError(reconnectTask.Exception!.GetBaseException(),
                                 "Failed to connect to VPN service");

            if (settingsTask.IsFaulted)
                _logger.LogError(settingsTask.Exception!.GetBaseException(),
                                 "Failed to fetch Coder Connect settings");

            // Don't attempt to connect if we failed to load credentials or reconnect.
            // This will prevent the app from trying to connect to the VPN service.
            dependenciesLoaded = false;
        }

        var attemptCoderConnection = settingsTask.Result?.ConnectOnLaunch ?? false;
        if (dependenciesLoaded && attemptCoderConnection)
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
        using var syncSessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        syncSessionCts.CancelAfter(TimeSpan.FromSeconds(10));
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
        _logger.LogInformation("handled notification activation: {Argument}", args.Argument);
        _userNotifier.HandleActivation(args);
    }

    private static void AddDefaultConfig(IConfigurationBuilder builder)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoderDesktop",
            LogFilename);
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [MutagenControllerConfigSection + ":MutagenExecutablePath"] = @"C:\mutagen.exe",

            ["Serilog:Using:0"] = "Serilog.Sinks.File",
            ["Serilog:MinimumLevel"] = DefaultLogLevel,
            ["Serilog:Enrich:0"] = "FromLogContext",
            ["Serilog:WriteTo:0:Name"] = "File",
            ["Serilog:WriteTo:0:Args:path"] = logPath,
            ["Serilog:WriteTo:0:Args:outputTemplate"] =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
            ["Serilog:WriteTo:0:Args:rollingInterval"] = "Day",

#if DEBUG
            ["Serilog:Using:1"] = "Serilog.Sinks.Debug",
            ["Serilog:Enrich:1"] = "FromLogContext",
            ["Serilog:WriteTo:1:Name"] = "Debug",
            ["Serilog:WriteTo:1:Args:outputTemplate"] =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
#endif
        });
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Coder.Desktop.App.Diagnostics;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Coder.Desktop.CoderSdk.Agent;
using Coder.Desktop.CoderSdk.Coder;
using Coder.Desktop.Vpn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private TrayWindow? _trayWindow;
    private TrayIconViewModel? _trayIconViewModel;
    private AvaloniaHostApplicationLifetime? _hostApplicationLifetime;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppBootstrapLogger.Warn("Unsupported application lifetime (not desktop style)");
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();

        // App infrastructure
        _hostApplicationLifetime = new AvaloniaHostApplicationLifetime(() => desktop.Shutdown());
        desktop.Exit += (_, _) => _hostApplicationLifetime.NotifyStopped();

        services.AddSingleton<IHostApplicationLifetime>(_hostApplicationLifetime);

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Core platform services
        services.AddSingleton<IDispatcher, AvaloniaDispatcher>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<ILauncherService, ProcessLauncherService>();
        services.AddSingleton<IWindowService, AvaloniaWindowService>();
        services.AddSingleton<IStartupManager, LinuxXdgStartupManager>();

        // Credentials + RPC
        services.AddSingleton<ICoderApiClientFactory, CoderApiClientFactory>();
        services.AddSingleton<IAgentApiClientFactory, AgentApiClientFactory>();
        services.AddSingleton<ICredentialBackend, LinuxSecretServiceBackend>();
        services.AddSingleton<ICredentialManager, CredentialManager>();

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("App.Avalonia currently supports Linux RPC transport only.");

        services.AddSingleton<IRpcClientTransport, UnixSocketClientTransport>();
        services.AddSingleton<IRpcController, RpcController>();

        // File sync (placeholder backend for Avalonia Linux host)
        services.AddSingleton<ISyncSessionController, UnavailableSyncSessionController>();

        // Settings and hostname metadata
        services.AddSingleton<ISettingsManager<CoderConnectSettings>, SettingsManager<CoderConnectSettings>>();
        services.AddSingleton<IHostnameSuffixGetter, HostnameSuffixGetter>();

        // View model factories
        services.AddSingleton<IAgentAppViewModelFactory, AgentAppViewModelFactory>();
        services.AddSingleton<IAgentViewModelFactory, AgentViewModelFactory>();

        // Tray and window view models
        services.AddTransient<TrayWindowDisconnectedViewModel>();
        services.AddTransient<TrayWindowLoginRequiredViewModel>();
        services.AddTransient<TrayWindowViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<FileSyncListViewModel>();

        services.AddSingleton<TrayWindow>();

        _services = services.BuildServiceProvider();

        var args = desktop.Args ?? [];
        var startMinimized = false;
        foreach (var arg in args)
        {
            if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--start-hidden", StringComparison.OrdinalIgnoreCase))
            {
                startMinimized = true;
                break;
            }
        }

        _trayWindow = _services.GetRequiredService<TrayWindow>();

        // Make TrayWindow the MainWindow so dialogs can use it as an owner.
        desktop.MainWindow = _trayWindow;

        // If launched with --minimized (e.g. XDG autostart), keep startup
        // behavior as tray-first. Otherwise, show the tray window so manual
        // launches are visible even when tray icon integration is missing.
        if (startMinimized)
        {
            AppBootstrapLogger.Info("Startup mode: minimized to tray (--minimized)");
            _trayWindow.RequestHideOnFirstOpen();
        }
        else
        {
            AppBootstrapLogger.Info("Startup mode: showing tray window");
        }

        _trayIconViewModel = new TrayIconViewModel(ToggleTrayWindow, () => desktop.Shutdown());
        ConfigureTrayIcons(_trayIconViewModel);

        _ = InitializeServicesAsync();
        _hostApplicationLifetime.NotifyStarted();

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeServicesAsync()
    {
        if (_services == null)
            return;

        var credentialManager = _services.GetRequiredService<ICredentialManager>();
        var rpcController = _services.GetRequiredService<IRpcController>();
        var settingsManager = _services.GetRequiredService<ISettingsManager<CoderConnectSettings>>();

        AppBootstrapLogger.Info("Initializing credentials and RPC connection...");

        var appStopping = _hostApplicationLifetime?.ApplicationStopping ?? CancellationToken.None;
        using var credentialLoadCts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);
        credentialLoadCts.CancelAfter(TimeSpan.FromSeconds(15));

        var loadCredentialsTask = credentialManager.LoadCredentials(credentialLoadCts.Token);
        var reconnectTask = ReconnectWithStartupRetryAsync(rpcController, appStopping);

        try
        {
            await Task.WhenAll(loadCredentialsTask, reconnectTask);
            AppBootstrapLogger.Info("Initial credential load and RPC reconnect completed");
        }
        catch
        {
            if (loadCredentialsTask.IsFaulted)
                AppBootstrapLogger.Error("Credential initialization failed", loadCredentialsTask.Exception?.GetBaseException());

            // reconnectTask logs its own errors internally; just note if it threw here
            if (reconnectTask.IsFaulted)
                AppBootstrapLogger.Error("Startup reconnect failed unexpectedly", reconnectTask.Exception?.GetBaseException());
        }

        var reconnectSucceeded = reconnectTask is { IsCompletedSuccessfully: true, Result: true };

        if (!reconnectSucceeded)
            AppBootstrapLogger.Warn("Startup continuing in disconnected state after retry exhaustion");

        try
        {
            await MaybeAutoStartVpnOnLaunchAsync(settingsManager, credentialManager, rpcController, reconnectSucceeded, appStopping);
        }
        catch (Exception ex)
        {
            AppBootstrapLogger.Error("ConnectOnLaunch failed", ex);
        }
    }

    private async Task<bool> ReconnectWithStartupRetryAsync(IRpcController rpcController, CancellationToken ct)
    {
        TimeSpan[] delays =
        [
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
        ];

        Exception? lastError = null;

        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(delays[attempt], ct);

            try
            {
                await rpcController.Reconnect(ct);
                AppBootstrapLogger.Info($"RPC reconnect succeeded on attempt {attempt + 1}/{delays.Length}");
                return true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastError = ex;
                AppBootstrapLogger.Warn($"RPC reconnect attempt {attempt + 1}/{delays.Length} failed: {ex.Message}");
            }
        }

        AppBootstrapLogger.Error("RPC reconnect exhausted startup retries", lastError);
        return false;
    }

    private async Task MaybeAutoStartVpnOnLaunchAsync(
        ISettingsManager<CoderConnectSettings> settingsManager,
        ICredentialManager credentialManager,
        IRpcController rpcController,
        bool reconnectSucceeded,
        CancellationToken ct)
    {
        var settings = await settingsManager.Read(ct);
        if (!settings.ConnectOnLaunch)
            return;

        if (!reconnectSucceeded)
        {
            AppBootstrapLogger.Info("ConnectOnLaunch skipped because startup reconnect did not succeed");
            return;
        }

        var creds = credentialManager.GetCachedCredentials();
        var rpc = rpcController.GetState();

        if (creds.State != CredentialState.Valid ||
            rpc.RpcLifecycle != RpcLifecycle.Connected ||
            rpc.VpnLifecycle != VpnLifecycle.Stopped)
        {
            AppBootstrapLogger.Info($"ConnectOnLaunch skipped (cred={creds.State}, rpc={rpc.RpcLifecycle}, vpn={rpc.VpnLifecycle})");
            return;
        }

        await rpcController.StartVpn(ct);
        AppBootstrapLogger.Info("ConnectOnLaunch started VPN successfully");
    }

    private void ConfigureTrayIcons(TrayIconViewModel trayIconViewModel)
    {
        // The tray icons are defined in App.axaml via the TrayIcon.Icons attached property.
        var icons = TrayIcon.GetIcons(this);
        if (icons is null)
        {
            AppBootstrapLogger.Warn("Tray icon collection is null; tray menu may be unavailable");
            return;
        }

        foreach (var trayIcon in icons)
        {
            // Ensure clicking the icon toggles the tray window.
            trayIcon.Clicked -= TrayIconOnClicked;
            trayIcon.Clicked += TrayIconOnClicked;

            // Keep the icon click behavior event-driven. Setting both Command and
            // Clicked can cause duplicate toggles on some Linux tray implementations.
            trayIcon.Command = null;

            if (trayIcon.Menu is NativeMenu menu)
            {
                foreach (var item in menu.Items)
                {
                    if (item is not NativeMenuItem nativeItem)
                        continue;

                    switch (nativeItem.Header?.ToString())
                    {
                        case "Show":
                            nativeItem.Command = trayIconViewModel.ShowWindowCommand;
                            break;
                        case "Exit":
                            nativeItem.Command = trayIconViewModel.ExitCommand;
                            break;
                    }
                }
            }
        }
    }

    private void TrayIconOnClicked(object? sender, EventArgs e)
    {
        ToggleTrayWindow();
    }

    private void ToggleTrayWindow()
    {
        if (_trayWindow is null)
            return;

        if (_trayWindow.IsVisible)
        {
            _trayWindow.Hide();
            return;
        }

        _trayWindow.ShowNearSystemTray();
    }
}

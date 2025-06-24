using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using NetSparkleUpdater;
using NetSparkleUpdater.AppCastHandlers;
using NetSparkleUpdater.AssemblyAccessors;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;
using SparkleLogger = NetSparkleUpdater.Interfaces.ILogger;

namespace Coder.Desktop.App.Services;

// TODO: add preview channel
public enum UpdateChannel
{
    Stable,
}

public static class UpdateChannelExtensions
{
    public static string ChannelName(this UpdateChannel channel)
    {
        switch (channel)
        {
            case UpdateChannel.Stable:
                return "stable";
            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
        }
    }
}

public class UpdaterConfig
{
    public bool Enable { get; set; } = true;
    [Required] public string AppCastUrl { get; set; } = "https://releases.coder.com/coder-desktop/windows/appcast.xml";
    [Required] public string PublicKeyBase64 { get; set; } = "NNWN4c+3PmMuAf2G1ERLlu0EwhzHfSiUugOt120hrH8=";
    // This preference forces an update channel to be used and prevents the
    // user from picking their own channel.
    public UpdateChannel? ForcedChannel { get; set; } = null;
}

public interface IUpdateController : IAsyncDisposable
{
    // Must be called from UI thread.
    public Task CheckForUpdatesNow();
}

public class SparkleUpdateController : IUpdateController, INotificationHandler
{
    internal const string NotificationHandlerName = "SparkleUpdateNotification";

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly ILogger<SparkleUpdateController> _logger;
    private readonly UpdaterConfig _config;
    private readonly IUserNotifier _userNotifier;
    private readonly IUIFactory _uiFactory;

    private readonly SparkleUpdater? _sparkle;

    public SparkleUpdateController(ILogger<SparkleUpdateController> logger, IOptions<UpdaterConfig> config, IUserNotifier userNotifier, IUIFactory uiFactory)
    {
        _logger = logger;
        _config = config.Value;
        _userNotifier = userNotifier;
        _uiFactory = uiFactory;

        _userNotifier.RegisterHandler(NotificationHandlerName, this);

        if (!_config.Enable)
        {
            _logger.LogInformation("updater disabled by policy");
            return;
        }

        _logger.LogInformation("updater enabled, creating NetSparkle instance");

        // This behavior differs from the macOS version of Coder Desktop, which
        // does not verify app cast signatures.
        //
        // Swift's Sparkle does not support verifying app cast signatures yet,
        // but we use this functionality on Windows for added security against
        // malicious release notes.
        var checker = new Ed25519Checker(SecurityMode.Strict,
            publicKey: _config.PublicKeyBase64,
            readFileBeingVerifiedInChunks: true);

        // Tell NetSparkle to store its configuration in the same directory as
        // our other config files.
        var appConfigDir = SettingsManagerUtils.AppSettingsDirectory();
        var sparkleConfigPath = Path.Combine(appConfigDir, "updater.json");
        var sparkleAssemblyAccessor = new AssemblyDiagnosticsAccessor(null); // null => use current executable path
        var sparkleConfig = new JSONConfiguration(sparkleAssemblyAccessor, sparkleConfigPath);

        _sparkle = new SparkleUpdater(_config.AppCastUrl, checker)
        {
            Configuration = sparkleConfig,
            // GitHub releases endpoint returns a random UUID as the filename,
            // so we tell NetSparkle to ignore it and use the last segment of
            // the URL instead.
            CheckServerFileName = false,
            LogWriter = new CoderSparkleLogger(logger),
            AppCastHelper = new CoderSparkleAppCastHelper(_config.ForcedChannel),
            UIFactory = uiFactory,
            UseNotificationToast = uiFactory.CanShowToastMessages(),
            RelaunchAfterUpdate = true,
        };

        _sparkle.CloseApplicationAsync += SparkleOnCloseApplicationAsync;

        // TODO: user preference for automatic checking. Remember to
        //       StopLoop/StartLoop if it changes.
#if !DEBUG
        _ = _sparkle.StartLoop(true, UpdateCheckInterval);
#endif
    }

    private static async Task SparkleOnCloseApplicationAsync()
    {
        await ((App)Application.Current).ExitApplication();
    }

    public async Task CheckForUpdatesNow()
    {
        if (_sparkle == null)
        {
            _ = new MessageWindow(
                "Updates disabled",
                "The built-in updater is disabled by policy.",
                "Coder Desktop Updater");
            return;
        }

        // NetSparkle will not open the UpdateAvailable window if it can send a
        // toast, even if the user requested the update. We work around this by
        // temporarily disabling toasts during this operation.
        var coderFactory = _uiFactory as CoderSparkleUIFactory;
        try
        {
            if (coderFactory is not null)
                coderFactory.ForceDisableToastMessages = true;

            await _sparkle.CheckForUpdatesAtUserRequest(true);
        }
        finally
        {
            if (coderFactory is not null)
                coderFactory.ForceDisableToastMessages = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _userNotifier.UnregisterHandler(NotificationHandlerName);
        _sparkle?.Dispose();
        return ValueTask.CompletedTask;
    }

    public void HandleNotificationActivation(IDictionary<string, string> args)
    {
        _ = CheckForUpdatesNow();
    }
}

public class CoderSparkleLogger(ILogger<SparkleUpdateController> logger) : SparkleLogger
{
    public void PrintMessage(string message, params object[]? arguments)
    {
        logger.LogInformation("[sparkle] " + message, arguments ?? []);
    }
}

public class CoderSparkleAppCastHelper(UpdateChannel? forcedChannel) : AppCastHelper
{
    // This might return some other OS if the user compiled the app for some
    // different arch, but the end result is the same: no updates will be found
    // for that arch.
    private static string CurrentOperatingSystem => $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    public override List<AppCastItem> FilterUpdates(List<AppCastItem> items)
    {
        items = base.FilterUpdates(items);

        // TODO: factor in user channel choice too once we have a settings page
        var channel = forcedChannel ?? UpdateChannel.Stable;
        return items.FindAll(i => i.Channel == channel.ChannelName() && i.OperatingSystem == CurrentOperatingSystem);
    }
}

// ReSharper disable once InconsistentNaming // the interface name is "UI", not "Ui"
public class CoderSparkleUIFactory(IUserNotifier userNotifier, IUpdaterUpdateAvailableViewModelFactory updateAvailableViewModelFactory) : IUIFactory
{
    public bool ForceDisableToastMessages;

    public bool HideReleaseNotes { get; set; }
    public bool HideSkipButton { get; set; }
    public bool HideRemindMeLaterButton { get; set; }

    // This stuff is ignored as we use our own template in the ViewModel
    // directly:
    string? IUIFactory.ReleaseNotesHTMLTemplate { get; set; }
    string? IUIFactory.AdditionalReleaseNotesHeaderHTML { get; set; }

    public IUpdateAvailable CreateUpdateAvailableWindow(List<AppCastItem> updates, ISignatureVerifier? signatureVerifier,
        string currentVersion = "", string appName = "Coder Desktop", bool isUpdateAlreadyDownloaded = false)
    {
        var viewModel = updateAvailableViewModelFactory.Create(
            updates,
            signatureVerifier,
            currentVersion,
            appName,
            isUpdateAlreadyDownloaded);

        var window = new UpdaterUpdateAvailableWindow(viewModel);
        if (HideReleaseNotes)
            (window as IUpdateAvailable).HideReleaseNotes();
        if (HideSkipButton)
            (window as IUpdateAvailable).HideSkipButton();
        if (HideRemindMeLaterButton)
            (window as IUpdateAvailable).HideRemindMeLaterButton();

        return window;
    }

    IDownloadProgress IUIFactory.CreateProgressWindow(string downloadTitle, string actionButtonTitleAfterDownload)
    {
        var viewModel = new UpdaterDownloadProgressViewModel();
        return new UpdaterDownloadProgressWindow(viewModel);
    }

    ICheckingForUpdates IUIFactory.ShowCheckingForUpdates()
    {
        return new UpdaterCheckingForUpdatesWindow();
    }

    void IUIFactory.ShowUnknownInstallerFormatMessage(string downloadFileName)
    {
        _ = new MessageWindow("Installer format error",
            $"The installer format for the downloaded file '{downloadFileName}' is unknown. Please check application logs for more information.",
            "Coder Desktop Updater");
    }

    void IUIFactory.ShowVersionIsUpToDate()
    {
        _ = new MessageWindow(
            "No updates available",
            "Coder Desktop is up to date!",
            "Coder Desktop Updater");
    }

    void IUIFactory.ShowVersionIsSkippedByUserRequest()
    {
        _ = new MessageWindow(
            "Update skipped",
            "You have elected to skip this update.",
            "Coder Desktop Updater");
    }

    void IUIFactory.ShowCannotDownloadAppcast(string? appcastUrl)
    {
        _ = new MessageWindow("Cannot fetch update information",
            $"Unable to download the updates manifest from '{appcastUrl}'. Please check your internet connection or firewall settings and try again.",
            "Coder Desktop Updater");
    }

    void IUIFactory.ShowDownloadErrorMessage(string message, string? appcastUrl)
    {
        _ = new MessageWindow("Download error",
            $"An error occurred while downloading the update. Please check your internet connection or firewall settings and try again.\n\n{message}",
            "Coder Desktop Updater");
    }

    bool IUIFactory.CanShowToastMessages()
    {
        return !ForceDisableToastMessages;
    }

    void IUIFactory.ShowToast(Action clickHandler)
    {
        // We disregard the Action passed to us by NetSparkle as it uses cached
        // data and does not perform a new update check. The
        // INotificationHandler is registered by SparkleUpdateController.
        _ = userNotifier.ShowActionNotification(
            "Coder Desktop",
            "Updates are available, click for more information.",
            SparkleUpdateController.NotificationHandlerName,
            null,
            CancellationToken.None);
    }

    void IUIFactory.Shutdown()
    {
        ((App)Application.Current).ExitApplication().Wait();
    }
}

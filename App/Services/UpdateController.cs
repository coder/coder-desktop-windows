using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using NetSparkleUpdater;
using NetSparkleUpdater.AppCastHandlers;
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

// TODO: SET THESE TO THE CORRECT SETTINGS
public class UpdaterConfig
{
    public bool EnableUpdater { get; set; } = true;
    //[Required] public string UpdateAppCastUrl { get; set; } = "https://releases.coder.com/coder-desktop/windows/appcast.xml";
    [Required] public string UpdateAppCastUrl { get; set; } = "http://localhost:8000/appcast.xml";
    [Required] public string UpdatePublicKeyBase64 { get; set; } = "Uxc0ir6j3GMhkL5D1O/W3lsD4BNk5puwM9hohNfm32k=";
    public UpdateChannel? ForcedUpdateChannel { get; set; } = null;
}

public interface IUpdateController : IAsyncDisposable
{
    // Must be called from UI thread.
    public Task CheckForUpdatesNow();
}

public class SparkleUpdateController : IUpdateController
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly ILogger<SparkleUpdateController> _logger;
    private readonly UpdaterConfig _config;
    private readonly IUIFactory _uiFactory;

    private readonly SparkleUpdater? _sparkle;

    public SparkleUpdateController(ILogger<SparkleUpdateController> logger, IOptions<UpdaterConfig> config, IUIFactory uiFactory)
    {
        _logger = logger;
        _config = config.Value;
        _uiFactory = uiFactory;

        if (!_config.EnableUpdater)
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
        // TODO: REENABLE STRICT CHECKING
        var checker = new Ed25519Checker(SecurityMode.Unsafe,
            publicKey: _config.UpdatePublicKeyBase64,
            readFileBeingVerifiedInChunks: true);

        _sparkle = new SparkleUpdater(_config.UpdateAppCastUrl, checker)
        {
            // TODO: custom Configuration for persistence, could just specify
            //       our own save path with JSONConfiguration TBH
            LogWriter = new CoderSparkleLogger(logger),
            AppCastHelper = new CoderSparkleAppCastHelper(logger, _config.ForcedUpdateChannel),
            UIFactory = uiFactory,
            UseNotificationToast = uiFactory.CanShowToastMessages(),
            RelaunchAfterUpdate = true,
        };

        _sparkle.CloseApplicationAsync += SparkleOnCloseApplicationAsync;

        // TODO: user preference for automatic checking
#if !DEBUG || true
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
                coderFactory.ForceDisableToasts = true;

            await _sparkle.CheckForUpdatesAtUserRequest(true);
        }
        finally
        {
            if (coderFactory is not null)
                coderFactory.ForceDisableToasts = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _sparkle?.Dispose();
        return ValueTask.CompletedTask;
    }
}

public class CoderSparkleLogger(ILogger<SparkleUpdateController> logger) : SparkleLogger
{
    public void PrintMessage(string message, params object[]? arguments)
    {
        logger.LogInformation("[sparkle] " + message, arguments ?? []);
    }
}

public class CoderSparkleAppCastHelper : AppCastHelper
{
    private readonly UpdateChannel? _forcedChannel;

    public CoderSparkleAppCastHelper(ILogger<SparkleUpdateController> logger, UpdateChannel? forcedChannel) : base()
    {
        _forcedChannel = forcedChannel;
    }

    public override List<AppCastItem> FilterUpdates(List<AppCastItem> items)
    {
        items = base.FilterUpdates(items);

        // TODO: factor in user choice too once we have a settings page
        var channel = _forcedChannel ?? UpdateChannel.Stable;
        return items.FindAll(i => i.Channel != null && i.Channel == channel.ChannelName());
    }
}

// ReSharper disable once InconsistentNaming // the interface name is "UI", not "Ui"
public class CoderSparkleUIFactory(IUserNotifier userNotifier, IUpdaterUpdateAvailableViewModelFactory updateAvailableViewModelFactory) : IUIFactory
{
    public bool ForceDisableToasts;

    bool IUIFactory.HideReleaseNotes { get; set; }
    bool IUIFactory.HideSkipButton { get; set; }
    bool IUIFactory.HideRemindMeLaterButton { get; set; }

    // This stuff is ignored as we use our own template in the ViewModel
    // directly:
    string? IUIFactory.ReleaseNotesHTMLTemplate { get; set; }
    string? IUIFactory.AdditionalReleaseNotesHeaderHTML { get; set; }

    public IUpdateAvailable CreateUpdateAvailableWindow(List<AppCastItem> updates, ISignatureVerifier? signatureVerifier,
        string currentVersion = "", string appName = "Coder Desktop", bool isUpdateAlreadyDownloaded = false)
    {
        IUIFactory factory = this;

        var viewModel = updateAvailableViewModelFactory.Create(
            updates,
            signatureVerifier,
            currentVersion,
            appName,
            isUpdateAlreadyDownloaded);

        var window = new UpdaterUpdateAvailableWindow(viewModel);
        if (factory.HideReleaseNotes)
            (window as IUpdateAvailable).HideReleaseNotes();
        if (factory.HideSkipButton)
            (window as IUpdateAvailable).HideSkipButton();
        if (factory.HideRemindMeLaterButton)
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
        return !ForceDisableToasts;
    }

    void IUIFactory.ShowToast(Action clickHandler)
    {
        userNotifier.ShowActionNotification(
            "Coder Desktop",
            "Updates are available, click for more information.",
            clickHandler,
            CancellationToken.None)
            .Wait();
    }

    void IUIFactory.Shutdown()
    {
        ((App)Application.Current).ExitApplication().Wait();
    }
}

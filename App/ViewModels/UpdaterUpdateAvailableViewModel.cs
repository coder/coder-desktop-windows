using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace Coder.Desktop.App.ViewModels;

public interface IUpdaterUpdateAvailableViewModelFactory
{
    public UpdaterUpdateAvailableViewModel Create(List<AppCastItem> updates, ISignatureVerifier? signatureVerifier, string currentVersion, string appName, bool isUpdateAlreadyDownloaded);
}

public class UpdaterUpdateAvailableViewModelFactory(ILogger<UpdaterUpdateAvailableViewModel> childLogger) : IUpdaterUpdateAvailableViewModelFactory
{
    public UpdaterUpdateAvailableViewModel Create(List<AppCastItem> updates, ISignatureVerifier? signatureVerifier, string currentVersion, string appName, bool isUpdateAlreadyDownloaded)
    {
        return new UpdaterUpdateAvailableViewModel(childLogger, updates, signatureVerifier, currentVersion, appName, isUpdateAlreadyDownloaded);
    }
}

public partial class UpdaterUpdateAvailableViewModel : ObservableObject
{
    private readonly ILogger<UpdaterUpdateAvailableViewModel> _logger;

    // All the unchanging stuff we get from NetSparkle:
    public readonly IReadOnlyList<AppCastItem> Updates;
    public readonly ISignatureVerifier? SignatureVerifier;
    public readonly string CurrentVersion;
    public readonly string AppName;
    public readonly bool IsUpdateAlreadyDownloaded;

    // Partial implementation of IUpdateAvailable:
    public UpdateAvailableResult Result { get; set; } = UpdateAvailableResult.None;
    // We only show the first update.
    public AppCastItem CurrentItem => Updates[0]; // always has at least one item
    public event UserRespondedToUpdate? UserResponded;

    // Other computed fields based on readonly data:
    public bool MissingCriticalUpdate => Updates.Any(u => u.IsCriticalUpdate);

    [ObservableProperty]
    public partial bool ReleaseNotesVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool RemindMeLaterButtonVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool SkipButtonVisible { get; set; } = true;

    public string MainText
    {
        get
        {
            var actionText = IsUpdateAlreadyDownloaded ? "install" : "download";
            return $"{AppName} {CurrentItem.Version} is now available (you have {CurrentVersion}). Would you like to {actionText} it now?";
        }
    }

    public UpdaterUpdateAvailableViewModel(ILogger<UpdaterUpdateAvailableViewModel> logger, List<AppCastItem> updates, ISignatureVerifier? signatureVerifier, string currentVersion, string appName, bool isUpdateAlreadyDownloaded)
    {
        if (updates.Count == 0)
            throw new InvalidOperationException("No updates available, cannot create UpdaterUpdateAvailableViewModel");

        _logger = logger;
        Updates = updates;
        SignatureVerifier = signatureVerifier;
        CurrentVersion = currentVersion;
        AppName = appName;
        IsUpdateAlreadyDownloaded = isUpdateAlreadyDownloaded;
    }

    public void HideReleaseNotes()
    {
        ReleaseNotesVisible = false;
    }

    public void HideRemindMeLaterButton()
    {
        RemindMeLaterButtonVisible = false;
    }

    public void HideSkipButton()
    {
        SkipButtonVisible = false;
    }

    public async Task<string> ChangelogHtml(AppCastItem item)
    {
        const string cssResourceName = "Coder.Desktop.App.Assets.changelog.css";
        const string htmlTemplate = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        html, body {
            margin: 0;
            padding: 6px;
            background: transparent;
            font-family: sans-serif; /* fallback */
            color: black;            /* fallback */
        }
        body {
            /* Slightly darken the background on all themes */
            background-color: rgba(0, 0, 0, 0.1);
        }
        body[data-theme=""dark""] {
            color: white; /* fallback */
        }
        .markdown-body {
            /* Slightly decrease all text size */
            font-size: 14px; /* default 16px */
        }
    </style>

    <style>
{{GITHUB_MARKDOWN_CSS}}
    </style>
</head>
<body data-theme=""{{THEME}}"">
    <div class=""markdown-body"">
        {{CONTENT}}
    </div>
</body>
</html>
";

        const string githubMarkdownCssToken = "{{GITHUB_MARKDOWN_CSS}}";
        const string themeToken = "{{THEME}}";
        const string contentToken = "{{CONTENT}}";

        // We load the CSS from an embedded asset since it's large.
        var css = "";
        try
        {
            await using var stream = typeof(App).Assembly.GetManifestResourceStream(cssResourceName)
                                     ?? throw new FileNotFoundException($"Embedded resource not found: {cssResourceName}");
            using var reader = new StreamReader(stream);
            css = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "failed to load changelog CSS theme from embedded asset, ignoring");
        }

        // We store the changelog in the description field, rather than using
        // the release notes URL to avoid extra requests.
        var innerHtml = item.Description;
        if (string.IsNullOrWhiteSpace(innerHtml))
        {
            innerHtml = "<p>No release notes available.</p>";
        }

        // The theme doesn't automatically update.
        var currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? "dark" : "light";
        return htmlTemplate
            .Replace(githubMarkdownCssToken, css)
            .Replace(themeToken, currentTheme)
            .Replace(contentToken, innerHtml);
    }

    public async Task Changelog_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WebView2 webView)
            return;

        // Start the engine with a custom user data folder. The default for
        // unpackaged WinUI 3 apps is to write to a subfolder in the app's
        // install directory, which is Program Files by default and not
        // writeable by the user.
        var userDataFolder = Path.Join(SettingsManagerUtils.AppSettingsDirectory(), "WebView2");
        _logger.LogDebug("Creating WebView2 user data folder at {UserDataFolder}", userDataFolder);
        Directory.CreateDirectory(userDataFolder);
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            userDataFolder,
            new CoreWebView2EnvironmentOptions());
        await webView.EnsureCoreWebView2Async(env);

        // Disable unwanted features.
        var settings = webView.CoreWebView2.Settings;
        settings.IsScriptEnabled = false;               // disables JS
        settings.AreHostObjectsAllowed = false;         // disables interaction with app code
#if !DEBUG
        settings.AreDefaultContextMenusEnabled = false; // disables right-click
        settings.AreDevToolsEnabled = false;
#endif
        settings.IsZoomControlEnabled = false;
        settings.IsStatusBarEnabled = false;

        // Hijack navigation to prevent links opening in the web view.
        webView.CoreWebView2.NavigationStarting += (_, e) =>
        {
            // webView.NavigateToString uses data URIs, so allow those to work.
            if (e.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                return;

            // Prevent the web view from trying to navigate to it.
            e.Cancel = true;

            // Launch HTTP or HTTPS URLs in the default browser.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri is { Scheme: "http" or "https" })
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
        };
        webView.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            // Prevent new windows from being launched (e.g. target="_blank").
            e.Handled = true;
            // Launch HTTP or HTTPS URLs in the default browser.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri is { Scheme: "http" or "https" })
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
        };

        var html = await ChangelogHtml(CurrentItem);
        webView.NavigateToString(html);
    }

    private void SendResponse(UpdateAvailableResult result)
    {
        Result = result;
        UserResponded?.Invoke(this, new UpdateResponseEventArgs(result, CurrentItem));
    }

    public void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SkipButtonVisible || MissingCriticalUpdate)
            return;
        SendResponse(UpdateAvailableResult.SkipUpdate);
    }

    public void RemindMeLaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RemindMeLaterButtonVisible || MissingCriticalUpdate)
            return;
        SendResponse(UpdateAvailableResult.RemindMeLater);
    }

    public void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        SendResponse(UpdateAvailableResult.InstallUpdate);
    }
}

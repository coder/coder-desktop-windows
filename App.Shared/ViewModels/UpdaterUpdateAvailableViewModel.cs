using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace Coder.Desktop.App.ViewModels;

public interface IUpdaterUpdateAvailableViewModelFactory
{
    public UpdaterUpdateAvailableViewModel Create(List<AppCastItem> updates, ISignatureVerifier? signatureVerifier,
        string currentVersion, string appName, bool isUpdateAlreadyDownloaded);
}

public class UpdaterUpdateAvailableViewModelFactory(ILogger<UpdaterUpdateAvailableViewModel> childLogger)
    : IUpdaterUpdateAvailableViewModelFactory
{
    public UpdaterUpdateAvailableViewModel Create(List<AppCastItem> updates, ISignatureVerifier? signatureVerifier,
        string currentVersion, string appName, bool isUpdateAlreadyDownloaded)
    {
        return new UpdaterUpdateAvailableViewModel(childLogger, updates, signatureVerifier, currentVersion, appName,
            isUpdateAlreadyDownloaded);
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
    private bool _releaseNotesVisible = true;

    [ObservableProperty]
    private bool _remindMeLaterButtonVisible = true;

    [ObservableProperty]
    private bool _skipButtonVisible = true;

    /// <summary>
    /// Whether the current app theme is dark.
    ///
    /// Replaces WinUI's Application.Current.RequestedTheme usage.
    /// </summary>
    [ObservableProperty]
    private bool _isDarkTheme = false;

    public string MainText
    {
        get
        {
            var actionText = IsUpdateAlreadyDownloaded ? "install" : "download";
            return
                $"{AppName} {CurrentItem.Version} is now available (you have {CurrentVersion}). Would you like to {actionText} it now?";
        }
    }

    public UpdaterUpdateAvailableViewModel(ILogger<UpdaterUpdateAvailableViewModel> logger, List<AppCastItem> updates,
        ISignatureVerifier? signatureVerifier, string currentVersion, string appName, bool isUpdateAlreadyDownloaded)
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

    public Task<string> ChangelogHtml(AppCastItem item)
    {
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

        // TODO: Avalonia - load and provide GitHub markdown CSS from UI layer.
        var css = "";

        // We store the changelog in the description field, rather than using
        // the release notes URL to avoid extra requests.
        var innerHtml = item.Description;
        if (string.IsNullOrWhiteSpace(innerHtml))
            innerHtml = "<p>No release notes available.</p>";

        // The theme doesn't automatically update.
        var currentTheme = IsDarkTheme ? "dark" : "light";

        var html = htmlTemplate
            .Replace(githubMarkdownCssToken, css)
            .Replace(themeToken, currentTheme)
            .Replace(contentToken, innerHtml);

        return Task.FromResult(html);
    }

    public Task Changelog_Loaded(object? sender, EventArgs e)
    {
        // TODO: Avalonia - implement WebView/Markdown rendering in UI layer.
        // WinUI used WebView2 and configured navigation + data folder.
        _logger.LogDebug("Changelog_Loaded is a no-op in App.Shared");
        return Task.CompletedTask;
    }

    private void SendResponse(UpdateAvailableResult result)
    {
        Result = result;
        UserResponded?.Invoke(this, new UpdateResponseEventArgs(result, CurrentItem));
    }

    public void SkipButton_Click()
    {
        if (!SkipButtonVisible || MissingCriticalUpdate)
            return;
        SendResponse(UpdateAvailableResult.SkipUpdate);
    }

    public void RemindMeLaterButton_Click()
    {
        if (!RemindMeLaterButtonVisible || MissingCriticalUpdate)
            return;
        SendResponse(UpdateAvailableResult.RemindMeLater);
    }

    public void InstallButton_Click()
    {
        SendResponse(UpdateAvailableResult.InstallUpdate);
    }
}

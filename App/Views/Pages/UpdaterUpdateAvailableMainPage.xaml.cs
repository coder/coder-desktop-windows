using System;
using System.Diagnostics;
using System.IO;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class UpdaterUpdateAvailableMainPage : Page
{
    public readonly UpdaterUpdateAvailableViewModel ViewModel;

    public UpdaterUpdateAvailableMainPage(UpdaterUpdateAvailableViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void Changelog_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WebView2 webView)
            return;

        // Start the engine with a custom user data folder. The default for
        // unpackaged WinUI 3 apps is to write to a subfolder in the app's
        // install directory, which is Program Files by default and not
        // writeable by the user.
        var userDataFolder = Path.Join(SettingsManagerUtils.AppSettingsDirectory(), "WebView2");
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

        var html = await ViewModel.ChangelogHtml(ViewModel.CurrentItem);
        webView.NavigateToString(html);
    }
}

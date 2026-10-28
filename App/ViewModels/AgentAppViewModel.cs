using System;
using System.Linq;
using Windows.System;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.Vpn.Proto;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl, bool isSubAgentApp = false,
        bool sessionTokenBlocked = false, bool confirmBeforeOpening = false);
}

public class AgentAppViewModelFactory(ILogger<AgentAppViewModel> childLogger, ICredentialManager credentialManager)
    : IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl, bool isSubAgentApp = false,
        bool sessionTokenBlocked = false, bool confirmBeforeOpening = false)
    {
        return new AgentAppViewModel(childLogger, credentialManager)
        {
            Id = id,
            Name = name,
            AppUri = appUri,
            IconUrl = iconUrl,
            IsSubAgentApp = isSubAgentApp,
            SessionTokenBlocked = sessionTokenBlocked,
            ConfirmBeforeOpening = confirmBeforeOpening,
        };
    }
}

public partial class AgentAppViewModel : ObservableObject, IModelUpdateable<AgentAppViewModel>
{
    private readonly ILogger<AgentAppViewModel> _logger;
    private readonly ICredentialManager _credentialManager;

    public required Uuid Id { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    public required partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    public required partial Uri AppUri { get; set; }

    /// <summary>True when the app belongs to a sub-agent, such as a dev container.</summary>
    public bool IsSubAgentApp { get; init; }

    /// <summary>True when the user must confirm before the app's link is opened.</summary>
    public bool ConfirmBeforeOpening { get; init; }

    /// <summary>True when the app link needs a session token but isn't trusted with one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    [NotifyPropertyChangedFor(nameof(IconOpacity))]
    public partial bool SessionTokenBlocked { get; set; }

    public double IconOpacity => SessionTokenBlocked ? 0.4 : 1.0;

    [ObservableProperty] public partial Uri? IconUrl { get; set; }

    [ObservableProperty] public partial ImageSource IconImageSource { get; set; }

    [ObservableProperty] public partial bool UseFallbackIcon { get; set; } = true;

    private string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name;

    public string Details =>
        DisplayName + ":\n\n" +
        (SessionTokenBlocked ? BlockedDetails : AppUri.ToString());

    private string BlockedDetails => SessionTokenLink.BlockedMessage + " " + (IsSubAgentApp
        ? "Fix the app's url in the dev container's devcontainer.json."
        : "Ask your template administrator to fix the app's url in the template's coder_app configuration.");

    public AgentAppViewModel(ILogger<AgentAppViewModel> logger, ICredentialManager credentialManager)
    {
        _logger = logger;
        _credentialManager = credentialManager;

        // Apply the icon URL to the icon image source when it is updated.
        IconImageSource = UpdateIcon();
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IconUrl))
                IconImageSource = UpdateIcon();
        };
    }

    public bool TryApplyChanges(AgentAppViewModel obj)
    {
        if (Id != obj.Id) return false;

        // To avoid spurious UI updates which cause flashing, don't actually
        // write to values unless they've changed.
        if (Name != obj.Name)
            Name = obj.Name;
        if (AppUri != obj.AppUri)
            AppUri = obj.AppUri;
        if (SessionTokenBlocked != obj.SessionTokenBlocked)
            SessionTokenBlocked = obj.SessionTokenBlocked;
        if (IconUrl != obj.IconUrl)
        {
            UseFallbackIcon = true;
            IconUrl = obj.IconUrl;
        }

        return true;
    }

    private ImageSource UpdateIcon()
    {
        if (IconUrl is null || (IconUrl.Scheme != "http" && IconUrl.Scheme != "https"))
        {
            UseFallbackIcon = true;
            return new BitmapImage();
        }

        // Determine what image source to use based on extension, use a
        // BitmapImage as last resort.
        var ext = IconUrl.AbsolutePath.Split('/').LastOrDefault()?.Split('.').LastOrDefault();
        // TODO: this is definitely a hack, URLs shouldn't need to end in .svg
        if (ext is "svg")
        {
            // TODO: Some SVGs like `/icon/cursor.svg` contain PNG data and
            //       don't render at all.
            var svg = new SvgImageSource(IconUrl);
            svg.Opened += (_, _) => _logger.LogDebug("app icon opened (svg): {uri}", IconUrl);
            svg.OpenFailed += (_, args) =>
                _logger.LogDebug("app icon failed to open (svg): {uri}: {Status}", IconUrl, args.Status);
            return svg;
        }

        var bitmap = new BitmapImage(IconUrl);
        bitmap.ImageOpened += (_, _) => _logger.LogDebug("app icon opened (bitmap): {uri}", IconUrl);
        bitmap.ImageFailed += (_, args) =>
            _logger.LogDebug("app icon failed to open (bitmap): {uri}: {ErrorMessage}", IconUrl, args.ErrorMessage);
        return bitmap;
    }

    public void OnImageOpened(object? sender, RoutedEventArgs e)
    {
        UseFallbackIcon = false;
    }

    public void OnImageFailed(object? sender, RoutedEventArgs e)
    {
        UseFallbackIcon = true;
    }

    [RelayCommand]
    private void OpenApp(object parameter)
    {
        try
        {
            var uri = AppUri;

            // http and https URLs should already be filtered out by
            // AgentViewModel, but as a second line of defence don't do session
            // token var replacement on those URLs.
            if (uri.Scheme is not "http" and not "https")
            {
                var cred = _credentialManager.GetCachedCredentials();
                // Check and replace the raw link, since Uri.ToString() unescapes it.
                if (cred.State is CredentialState.Valid && cred.ApiToken is not null &&
                    SessionTokenLink.NeedsSessionToken(uri.OriginalString))
                    uri = new Uri(SessionTokenLink.Substitute(uri.OriginalString, cred.CoderUrl, cred.ApiToken,
                        IsSubAgentApp) ?? throw new Exception(SessionTokenLink.BlockedMessage));
            }

            if (uri.ToString().Contains(SessionTokenLink.Placeholder))
                throw new Exception(
                    $"URI contains {SessionTokenLink.Placeholder} variable but could not be replaced (http and https URLs cannot contain {SessionTokenLink.Placeholder})");

            if (!ConfirmBeforeOpening)
            {
                _ = Launcher.LaunchUriAsync(uri);
                return;
            }

            var open = new Button
            {
                Content = "Open",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var flyout = ShowFlyout(parameter, new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Open {DisplayName}?\n\nThis opens a {uri.Scheme}: link in another app on your computer.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    open,
                },
            });
            open.Click += (_, _) =>
            {
                flyout?.Hide();
                _ = Launcher.LaunchUriAsync(uri);
            };
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "could not parse or launch app");
            ShowFlyout(parameter, new TextBlock
            {
                Text = $"Could not open app: {e.Message}",
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private static Flyout? ShowFlyout(object parameter, UIElement content)
    {
        if (parameter is not FrameworkElement frameworkElement) return null;
        var flyout = new Flyout
        {
            Content = new Border { Margin = new Thickness(4), Child = content },
            FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
            {
                Setters =
                {
                    new Setter(ScrollViewer.HorizontalScrollModeProperty, ScrollMode.Disabled),
                    new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled),
                },
            },
        };
        FlyoutBase.SetAttachedFlyout(frameworkElement, flyout);
        FlyoutBase.ShowAttachedFlyout(frameworkElement);
        return flyout;
    }
}

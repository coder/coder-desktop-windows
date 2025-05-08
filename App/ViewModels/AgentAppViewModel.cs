using System;
using System.Linq;
using Windows.System;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
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
    public AgentAppViewModel Create(Uuid id, string name, string appUri, Uri? iconUrl);
}

public class AgentAppViewModelFactory(ILogger<AgentAppViewModel> childLogger, ICredentialManager credentialManager)
    : IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, string appUri, Uri? iconUrl)
    {
        return new AgentAppViewModel(childLogger, credentialManager)
        {
            Id = id,
            Name = name,
            AppUri = appUri,
            IconUrl = iconUrl,
        };
    }
}

public partial class AgentAppViewModel : ObservableObject, IModelUpdateable<AgentAppViewModel>
{
    private const string SessionTokenUriVar = "$SESSION_TOKEN";

    private readonly ILogger<AgentAppViewModel> _logger;
    private readonly ICredentialManager _credentialManager;

    public required Uuid Id { get; init; }

    [ObservableProperty] public required partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    public required partial string AppUri { get; set; }

    [ObservableProperty] public partial Uri? IconUrl { get; set; }

    [ObservableProperty] public partial ImageSource IconImageSource { get; set; }

    [ObservableProperty] public partial bool UseFallbackIcon { get; set; } = true;

    public string Details =>
        (string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name) + ":\n\n" + AppUri;

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
            var uriString = AppUri;
            var cred = _credentialManager.GetCachedCredentials();
            if (cred.State is CredentialState.Valid && cred.ApiToken is not null)
                uriString = uriString.Replace(SessionTokenUriVar, cred.ApiToken);
            if (uriString.Contains(SessionTokenUriVar))
                throw new Exception($"URI contains {SessionTokenUriVar} variable but could not be replaced");

            var uri = new Uri(uriString);
            _ = Launcher.LaunchUriAsync(uri);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "could not parse or launch app");

            if (parameter is not FrameworkElement frameworkElement) return;
            var flyout = new Flyout
            {
                Content = new TextBlock
                {
                    Text = $"Could not open app: {e.Message}",
                    Margin = new Thickness(4),
                    TextWrapping = TextWrapping.Wrap,
                },
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
        }
    }
}

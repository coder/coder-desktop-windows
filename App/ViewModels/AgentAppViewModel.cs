using System;
using System.Linq;
using Windows.System;
using Coder.Desktop.App.Utils;
using Coder.Desktop.Vpn.Proto;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl);
}

public class AgentAppViewModelFactory : IAgentAppViewModelFactory
{
    private readonly ILogger<AgentAppViewModel> _childLogger;

    public AgentAppViewModelFactory(ILogger<AgentAppViewModel> childLogger)
    {
        _childLogger = childLogger;
    }

    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl)
    {
        return new AgentAppViewModel(_childLogger)
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
    private readonly ILogger<AgentAppViewModel> _logger;

    public required Uuid Id { get; init; }

    [ObservableProperty] public required partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    public required partial Uri AppUri { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageSource))]
    public partial Uri? IconUrl { get; set; }

    [ObservableProperty] public partial bool UseFallbackIcon { get; set; } = true;

    public string Details =>
        (string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name) + ":\n\n" + AppUri;

    public ImageSource ImageSource
    {
        get
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
    }

    public AgentAppViewModel(ILogger<AgentAppViewModel> logger)
    {
        _logger = logger;
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

    public void OnImageOpened(object? sender, RoutedEventArgs e)
    {
        UseFallbackIcon = false;
    }

    public void OnImageFailed(object? sender, RoutedEventArgs e)
    {
        UseFallbackIcon = true;
    }

    [RelayCommand]
    private void OpenApp()
    {
        try
        {
            _ = Launcher.LaunchUriAsync(AppUri);
        }
        catch
        {
            // TODO: log/notify
        }
    }
}

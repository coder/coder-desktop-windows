using System;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.CoderSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl);
}

public class AgentAppViewModelFactory(
    ILogger<AgentAppViewModel> childLogger,
    ICredentialManager credentialManager,
    ILauncherService launcherService,
    IWindowService windowService)
    : IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl)
    {
        return new AgentAppViewModel(childLogger, credentialManager, launcherService, windowService)
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
    private readonly ILauncherService _launcherService;
    private readonly IWindowService _windowService;

    public required Uuid Id { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    private string _name = default!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    private Uri _appUri = default!;

    [ObservableProperty]
    private Uri? _iconUrl = default!;

    /// <summary>
    /// UI framework-specific image type (Avalonia IImage, WinUI ImageSource, etc.).
    ///
    /// In App.Shared we keep this as <see cref="object"/>.
    /// </summary>
    [ObservableProperty]
    private object? _iconImageSource = default!;

    [ObservableProperty]
    private bool _useFallbackIcon = true;

    public string Details =>
        (string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name) + ":\n\n" + AppUri;

    public AgentAppViewModel(
        ILogger<AgentAppViewModel> logger,
        ICredentialManager credentialManager,
        ILauncherService launcherService,
        IWindowService windowService)
    {
        _logger = logger;
        _credentialManager = credentialManager;
        _launcherService = launcherService;
        _windowService = windowService;

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
        if (Id != obj.Id)
            return false;

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

    private object? UpdateIcon()
    {
        if (IconUrl is null || (IconUrl.Scheme != "http" && IconUrl.Scheme != "https"))
        {
            UseFallbackIcon = true;
            return null;
        }

        // In App.Shared we don't construct UI-framework image types.
        // The UI layer can decide how to load/render this URI (SVG/bitmap/etc).
        UseFallbackIcon = true;
        return IconUrl;
    }

    public void OnImageOpened(object? sender, EventArgs e)
    {
        UseFallbackIcon = false;
    }

    public void OnImageFailed(object? sender, EventArgs e)
    {
        UseFallbackIcon = true;
    }

    [RelayCommand]
    private async Task OpenApp(object? parameter)
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
                if (cred.State is CredentialState.Valid && cred.ApiToken is not null)
                    uri = new Uri(uri.ToString().Replace(SessionTokenUriVar, cred.ApiToken));
            }

            if (uri.ToString().Contains(SessionTokenUriVar))
                throw new Exception(
                    $"URI contains {SessionTokenUriVar} variable but could not be replaced (http and https URLs cannot contain {SessionTokenUriVar})");

            await _launcherService.LaunchUriAsync(uri);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "could not parse or launch app");
            _windowService.ShowMessageWindow("Could not open app", e.Message, "Coder Connect");
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public enum SignInStage
{
    Url,
    Token,
}

/// <summary>
/// The View Model backing the sign in window and all its associated pages.
/// </summary>
public partial class SignInViewModel : ObservableObject
{
    private readonly ICredentialManager _credentialManager;
    private readonly IDispatcher _dispatcher;
    private readonly IWindowService _windowService;

    /// <summary>
    /// Raised when the view should close itself (e.g. after successful sign-in).
    /// </summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoderUrlError))]
    [NotifyPropertyChangedFor(nameof(GenTokenUrl))]
    private string _coderUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoderUrlError))]
    private bool _coderUrlTouched = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiTokenError))]
    private string _apiToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiTokenError))]
    private bool _apiTokenTouched = false;

    [ObservableProperty]
    private bool _signInLoading = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUrlStage))]
    [NotifyPropertyChangedFor(nameof(IsTokenStage))]
    private SignInStage _stage = SignInStage.Url;

    public bool IsUrlStage => Stage == SignInStage.Url;
    public bool IsTokenStage => Stage == SignInStage.Token;

    public string? CoderUrlError => CoderUrlTouched ? _coderUrlError : null;

    private string? _coderUrlError
    {
        get
        {
            if (!Uri.TryCreate(CoderUrl, UriKind.Absolute, out var uri))
                return "Invalid URL";
            if (uri.Scheme is not "http" and not "https")
                return "Must be a HTTP or HTTPS URL";
            if (uri.PathAndQuery != "/")
                return "Must be a root URL with no path or query";
            return null;
        }
    }

    public string? ApiTokenError => ApiTokenTouched ? _apiTokenError : null;

    private string? _apiTokenError => string.IsNullOrWhiteSpace(ApiToken) ? "Invalid token" : null;

    public Uri GenTokenUrl
    {
        get
        {
            // In case somehow the URL is invalid, just default to coder.com.
            try
            {
                var baseUri = new Uri(CoderUrl.Trim());
                return new Uri(baseUri, "/cli-auth");
            }
            catch
            {
                return new Uri("https://coder.com");
            }
        }
    }

    public SignInViewModel(ICredentialManager credentialManager, IDispatcher dispatcher, IWindowService windowService)
    {
        _credentialManager = credentialManager;
        _dispatcher = dispatcher;
        _windowService = windowService;

        // Load the previously used Coder URL in the background.
        _ = LoadExistingCoderUrl();
    }

    public void CoderUrl_Loaded(object? sender, EventArgs e)
    {
        _ = LoadExistingCoderUrl();
    }

    private async Task LoadExistingCoderUrl()
    {
        try
        {
            var url = await _credentialManager.GetSignInUri();
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.Post(() => ApplyLoadedCoderUrl(url));
                return;
            }

            ApplyLoadedCoderUrl(url);
        }
        catch
        {
            // ignored
        }
    }

    private void ApplyLoadedCoderUrl(string url)
    {
        if (CoderUrlTouched)
            return;

        CoderUrl = url;
        CoderUrlTouched = true;
    }

    public void CoderUrl_FocusLost(object? sender, EventArgs e)
    {
        CoderUrlTouched = true;
    }

    public void ApiToken_FocusLost(object? sender, EventArgs e)
    {
        ApiTokenTouched = true;
    }

    [RelayCommand]
    public void UrlPage_Next()
    {
        CoderUrlTouched = true;
        if (_coderUrlError != null)
            return;

        Stage = SignInStage.Token;
    }

    [RelayCommand]
    public void TokenPage_Back()
    {
        ApiToken = "";
        Stage = SignInStage.Url;
    }

    [RelayCommand]
    public async Task TokenPage_SignIn()
    {
        CoderUrlTouched = true;
        ApiTokenTouched = true;
        if (_coderUrlError != null || _apiTokenError != null)
            return;

        try
        {
            SignInLoading = true;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _credentialManager.SetCredentials(CoderUrl.Trim(), ApiToken.Trim(), cts.Token);

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            _windowService.ShowMessageWindow("Failed to sign in", e.ToString(), "Coder Connect");
        }
        finally
        {
            SignInLoading = false;
        }
    }
}

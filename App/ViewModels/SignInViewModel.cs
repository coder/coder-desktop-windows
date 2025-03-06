using System;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.ViewModels;

/// <summary>
///     The View Model backing the sign in window and all its associated pages.
/// </summary>
public partial class SignInViewModel : ObservableObject
{
    private readonly ICredentialManager _credentialManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoderUrlError))]
    [NotifyPropertyChangedFor(nameof(GenTokenUrl))]
    public partial string CoderUrl { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoderUrlError))]
    public partial bool CoderUrlTouched { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiTokenError))]
    public partial string ApiToken { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiTokenError))]
    public partial bool ApiTokenTouched { get; set; } = false;

    [ObservableProperty] public partial bool SignInLoading { get; set; } = false;

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
            // The HyperlinkButton will crash the entire app if the URL is
            // invalid.
            try
            {
                var baseUri = new Uri(CoderUrl.Trim());
                var cliAuthUri = new Uri(baseUri, "/cli-auth");
                return cliAuthUri;
            }
            catch
            {
                return new Uri("https://coder.com");
            }
        }
    }

    public SignInViewModel(ICredentialManager credentialManager)
    {
        _credentialManager = credentialManager;
        CoderUrl = _credentialManager.GetSignInUri() ?? "";
        if (!string.IsNullOrWhiteSpace(CoderUrl)) CoderUrlTouched = true;
    }

    public void CoderUrl_FocusLost(object sender, RoutedEventArgs e)
    {
        CoderUrlTouched = true;
    }

    public void ApiToken_FocusLost(object sender, RoutedEventArgs e)
    {
        ApiTokenTouched = true;
    }

    [RelayCommand]
    public void UrlPage_Next(SignInWindow signInWindow)
    {
        CoderUrlTouched = true;
        if (_coderUrlError != null) return;
        signInWindow.NavigateToTokenPage();
    }

    [RelayCommand]
    public void TokenPage_Back(SignInWindow signInWindow)
    {
        ApiToken = "";
        signInWindow.NavigateToUrlPage();
    }

    [RelayCommand]
    public async Task TokenPage_SignIn(SignInWindow signInWindow)
    {
        CoderUrlTouched = true;
        ApiTokenTouched = true;
        if (_coderUrlError != null || _apiTokenError != null) return;

        try
        {
            SignInLoading = true;

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _credentialManager.SetCredentials(CoderUrl.Trim(), ApiToken.Trim(), cts.Token);

            signInWindow.Close();
        }
        catch (Exception e)
        {
            var dialog = new ContentDialog
            {
                Title = "Failed to sign in",
                Content = $"{e}",
                CloseButtonText = "Ok",
                XamlRoot = signInWindow.Content.XamlRoot,
            };
            _ = await dialog.ShowAsync();
        }
        finally
        {
            SignInLoading = false;
        }
    }
}

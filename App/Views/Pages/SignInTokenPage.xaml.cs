using System;
using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Coder.Desktop.App.Views.Pages;

/// <summary>
///     A sign in page to accept the user's Coder token.
/// </summary>
public sealed partial class SignInTokenPage : Page
{
    public readonly SignInViewModel ViewModel;

    public SignInTokenPage(SignInViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
    }

    private void ApiToken_FocusLost(object sender, RoutedEventArgs e)
    {
        ViewModel.ApiToken_FocusLost(sender, EventArgs.Empty);
    }

    private async void PasswordBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await ViewModel.TokenPage_SignIn();
            e.Handled = true;
        }
    }
}

using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Coder.Desktop.App.Views.Pages;

/// <summary>
///     A login page to enter the Coder Server URL
/// </summary>
public sealed partial class SignInUrlPage : Page
{
    public readonly SignInViewModel ViewModel;
    public readonly SignInWindow SignInWindow;

    public SignInUrlPage(SignInWindow parent, SignInViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SignInWindow = parent;
    }

    private void TextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if(e.Key == VirtualKey.Enter)
        {
            ViewModel.CoderUrl = ((TextBox)sender).Text;
            ViewModel.UrlPage_Next(SignInWindow);
            e.Handled = true;
        }
    }
}

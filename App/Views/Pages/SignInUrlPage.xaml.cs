using System;
using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Coder.Desktop.App.Views.Pages;

/// <summary>
///     A login page to enter the Coder Server URL
/// </summary>
public sealed partial class SignInUrlPage : Page
{
    public readonly SignInViewModel ViewModel;

    public SignInUrlPage(SignInViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
    }

    private void CoderUrl_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.CoderUrl_Loaded(sender, EventArgs.Empty);
        // Move the caret to the end of any pre-populated URL.
        if (sender is TextBox textBox)
            textBox.SelectionStart = textBox.Text.Length;
    }

    private void CoderUrl_FocusLost(object sender, RoutedEventArgs e)
    {
        ViewModel.CoderUrl_FocusLost(sender, EventArgs.Empty);
    }

    private void TextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.UrlPage_Next();
            e.Handled = true;
        }
    }
}

using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

/// <summary>
/// A sign in page to accept the user's Coder token.
/// </summary>
public partial class SignInTokenPage : UserControl
{
    public SignInViewModel? ViewModel { get; private set; }

    public SignInTokenPage()
    {
        InitializeComponent();
    }

    public SignInTokenPage(SignInViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void ApiTokenTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ApiToken_FocusLost(sender, EventArgs.Empty);
    }

    private async void ApiTokenTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ViewModel is not null)
                await ViewModel.TokenPage_SignIn();
            e.Handled = true;
        }
    }

    private void GenerateTokenButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.GenTokenUrl is not { } uri)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignored
        }
    }
}

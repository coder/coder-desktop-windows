using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

/// <summary>
/// A login page to enter the Coder Server URL.
/// </summary>
public partial class SignInUrlPage : UserControl
{
    public SignInViewModel? ViewModel { get; private set; }

    public SignInUrlPage()
    {
        InitializeComponent();
    }

    public SignInUrlPage(SignInViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void CoderUrlTextBox_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ViewModel?.CoderUrl_Loaded(sender, EventArgs.Empty);
    }

    private void CoderUrlTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CoderUrl_FocusLost(sender, EventArgs.Empty);
    }

    private void CoderUrlTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel?.UrlPage_Next();
            e.Handled = true;
        }
    }
}

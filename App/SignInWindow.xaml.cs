using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Coder.Desktop.App.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;

namespace Coder.Desktop.App;

/// <summary>
/// The dialog window to allow the user to sign into their Coder server.
/// </summary>
public sealed partial class SignInWindow : Window
{
    private const double WIDTH = 600.0;
    private const double HEIGHT = 300.0;

    private readonly SignInViewModel viewModel;
    public SignInWindow(SignInViewModel vm)
    {
        viewModel = vm;
        InitializeComponent();
        var urlPage = new SignInURLPage(this, viewModel);
        Content = urlPage;
        ResizeWindow();
    }

    [RelayCommand]
    public void NavigateToTokenPage()
    {
        var tokenPage = new SignInTokenPage(this, viewModel);
        Content = tokenPage;
    }

    [RelayCommand]
    public void NavigateToURLPage()
    {
        var urlPage = new SignInURLPage(this, viewModel);
        Content = urlPage;
    }

    private void ResizeWindow()
    {
        var scale = DisplayScale.WindowScale(this);
        var height = (int)(HEIGHT * scale);
        var width = (int)(WIDTH * scale);
        AppWindow.Resize(new SizeInt32(width, height));
    }
}

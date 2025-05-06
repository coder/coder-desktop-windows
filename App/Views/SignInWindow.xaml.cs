using System;
using Windows.Graphics;
using Coder.Desktop.App.Controls;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Coder.Desktop.App.Views;

/// <summary>
///     The dialog window to allow the user to sign in to their Coder server.
/// </summary>
public sealed partial class SignInWindow : Window
{
    private const double WIDTH = 500.0;

    private readonly SignInUrlPage _signInUrlPage;
    private readonly SignInTokenPage _signInTokenPage;

    public SignInWindow(SignInViewModel viewModel)
    {
        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);
        SystemBackdrop = new DesktopAcrylicBackdrop();
        RootFrame.SizeChanged += RootFrame_SizeChanged;

        _signInUrlPage = new SignInUrlPage(this, viewModel);
        _signInTokenPage = new SignInTokenPage(this, viewModel);

        // Prevent the window from being resized.
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
            throw new Exception("Failed to get OverlappedPresenter for window");
        presenter.IsMaximizable = false;
        presenter.IsResizable = false;

        NavigateToUrlPage();
        ResizeWindow();
        MoveWindowToCenterOfDisplay();
    }

    public void NavigateToTokenPage()
    {
        RootFrame.SetPage(_signInTokenPage);
    }

    public void NavigateToUrlPage()
    {
        RootFrame.SetPage(_signInUrlPage);
    }

    private void RootFrame_SizeChanged(object sender, SizedFrameEventArgs e)
    {
        ResizeWindow(e.NewSize.Height);
    }

    private void ResizeWindow()
    {
        ResizeWindow(RootFrame.GetContentSize().Height);
    }

    private void ResizeWindow(double height)
    {
        if (height <= 0) height = 100; // will be resolved next frame typically

        var scale = DisplayScale.WindowScale(this);
        var newWidth = (int)(WIDTH * scale);
        var newHeight = (int)(height * scale);
        AppWindow.ResizeClient(new SizeInt32(newWidth, newHeight));
    }

    private void MoveWindowToCenterOfDisplay()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var x = (workArea.Width - AppWindow.Size.Width) / 2;
        var y = (workArea.Height - AppWindow.Size.Height) / 2;
        if (x < 0 || y < 0) return;
        AppWindow.Move(new PointInt32(x, y));
    }
}

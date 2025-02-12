using Windows.Graphics;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Coder.Desktop.App.Views;

/// <summary>
///     The dialog window to allow the user to sign into their Coder server.
/// </summary>
public sealed partial class SignInWindow : Window
{
    private const double WIDTH = 600.0;
    private const double HEIGHT = 300.0;

    private readonly SignInUrlPage _signInUrlPage;
    private readonly SignInTokenPage _signInTokenPage;

    public SignInWindow(SignInViewModel viewModel)
    {
        InitializeComponent();
        _signInUrlPage = new SignInUrlPage(this, viewModel);
        _signInTokenPage = new SignInTokenPage(this, viewModel);

        NavigateToUrlPage();
        ResizeWindow();
        MoveWindowToCenterOfDisplay();
    }

    public void NavigateToTokenPage()
    {
        RootFrame.Content = _signInTokenPage;
    }

    public void NavigateToUrlPage()
    {
        RootFrame.Content = _signInUrlPage;
    }

    private void ResizeWindow()
    {
        var scale = DisplayScale.WindowScale(this);
        var height = (int)(HEIGHT * scale);
        var width = (int)(WIDTH * scale);
        AppWindow.Resize(new SizeInt32(width, height));
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

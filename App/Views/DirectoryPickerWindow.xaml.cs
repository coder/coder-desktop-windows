using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Xaml.Media;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class DirectoryPickerWindow : WindowEx
{
    public DirectoryPickerWindow(DirectoryPickerViewModel viewModel)
    {
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();

        viewModel.Initialize(this, DispatcherQueue);
        RootFrame.Content = new DirectoryPickerMainPage(viewModel);

        // TODO: this should appear near the mouse instead, similar to the tray window
        this.CenterOnScreen();
    }
}

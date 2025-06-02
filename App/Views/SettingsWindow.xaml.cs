using Coder.Desktop.App.Utils;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Xaml.Media;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class SettingsWindow : WindowEx
{
    public readonly SettingsViewModel ViewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);

        SystemBackdrop = new DesktopAcrylicBackdrop();

        RootFrame.Content = new SettingsMainPage(ViewModel);

        this.CenterOnScreen();
    }
}

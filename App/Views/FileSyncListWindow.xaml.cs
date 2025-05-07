using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Xaml.Media;
using WinUIEx;
using Coder.Desktop.App.Utils;

namespace Coder.Desktop.App.Views;

public sealed partial class FileSyncListWindow : WindowEx
{
    public readonly FileSyncListViewModel ViewModel;

    public FileSyncListWindow(FileSyncListViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);

        SystemBackdrop = new DesktopAcrylicBackdrop();

        ViewModel.Initialize(this, DispatcherQueue);
        RootFrame.Content = new FileSyncListMainPage(ViewModel);

        this.CenterOnScreen();
    }

}

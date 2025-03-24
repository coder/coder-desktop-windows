using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Xaml.Media;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class FileSyncListWindow : WindowEx
{
    public readonly FileSyncListViewModel ViewModel;

    public FileSyncListWindow(FileSyncListViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();

        ViewModel.Initialize(DispatcherQueue);
        RootFrame.Content = new FileSyncListMainPage(ViewModel, this);

        this.CenterOnScreen();
    }
}

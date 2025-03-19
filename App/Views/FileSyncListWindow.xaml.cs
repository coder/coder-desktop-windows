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
        ViewModel.OnFileSyncListStale += ViewModel_OnFileSyncListStale;

        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();

        ViewModel.Initialize(DispatcherQueue);
        RootFrame.Content = new FileSyncListMainPage(ViewModel, this);

        this.CenterOnScreen();
    }

    private void ViewModel_OnFileSyncListStale()
    {
        // TODO: Fix this. I got a weird memory corruption exception when it
        //       fired immediately on start. Maybe we should schedule it for
        //       next frame or something.
        //Close()
    }
}

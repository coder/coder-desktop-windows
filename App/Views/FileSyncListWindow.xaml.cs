using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WinUIEx;
using Microsoft.UI.Windowing;
using System;
using System.IO;
using Coder.Desktop.App.Controls;

namespace Coder.Desktop.App.Views;

public sealed partial class FileSyncListWindow : WindowEx
{
    public readonly FileSyncListViewModel ViewModel;

    public FileSyncListWindow(FileSyncListViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();

        ViewModel.Initialize(this, DispatcherQueue);
        RootFrame.Content = new FileSyncListMainPage(ViewModel);

        TitleBarIcon.SetTitlebarIcon(this);

        this.CenterOnScreen();
    }

}

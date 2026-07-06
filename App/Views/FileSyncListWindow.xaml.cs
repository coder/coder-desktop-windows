using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Coder.Desktop.App.Utils;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class FileSyncListWindow : WindowEx
{
    public readonly FileSyncListViewModel ViewModel;

    private DirectoryPickerWindow? _remotePickerWindow;

    public FileSyncListWindow(FileSyncListViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);

        // Provide the WinUI implementations of the UI hooks the shared
        // ViewModel needs.
        ViewModel.LocalFolderPicker = OpenLocalFolderPickerAsync;
        ViewModel.ConfirmTerminateSessionAsync = ConfirmTerminateSessionAsync;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.Dispose();
        };

        RootFrame.Content = new FileSyncListMainPage(ViewModel);

        this.CenterOnScreen();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileSyncListViewModel.RemotePathPickerViewModel))
            return;

        var pickerViewModel = ViewModel.RemotePathPickerViewModel;
        if (pickerViewModel is null)
        {
            _remotePickerWindow?.Close();
            _remotePickerWindow = null;
            return;
        }

        if (_remotePickerWindow is not null)
        {
            _remotePickerWindow.Activate();
            return;
        }

        _remotePickerWindow = new DirectoryPickerWindow(pickerViewModel);
        _remotePickerWindow.Closed += (_, _) => _remotePickerWindow = null;
        _remotePickerWindow.SetParent(this);
        _remotePickerWindow.Activate();
    }

    private async Task<string?> OpenLocalFolderPickerAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var path = await picker.PickSingleFolderAsync();
        return path?.Path;
    }

    private async Task<bool> ConfirmTerminateSessionAsync(string identifier)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "Terminate sync session",
            Content = "Are you sure you want to terminate this sync session?",
            PrimaryButtonText = "Terminate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        var res = await confirmDialog.ShowAsync();
        return res is ContentDialogResult.Primary;
    }
}

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views;

public partial class FileSyncListWindow : Window
{
    private FileSyncListViewModel? _vm;
    private DirectoryPickerWindow? _remotePickerWindow;

    public FileSyncListWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachViewModel();
        Closed += (_, _) =>
        {
            DetachViewModel();
            _remotePickerWindow?.Close();
            _remotePickerWindow = null;
        };

        AttachViewModel();
    }

    public FileSyncListWindow(FileSyncListViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void AttachViewModel()
    {
        DetachViewModel();

        _vm = DataContext as FileSyncListViewModel;
        if (_vm is null)
            return;

        _vm.PropertyChanged += VmOnPropertyChanged;
        _vm.LocalFolderPicker = PickLocalFolderAsync;
        SyncRemotePickerWindow();
    }

    private void DetachViewModel()
    {
        if (_vm is null)
            return;

        _vm.PropertyChanged -= VmOnPropertyChanged;
        _vm.LocalFolderPicker = null;
        _vm = null;
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileSyncListViewModel.RemotePathPickerViewModel))
            SyncRemotePickerWindow();
    }

    private async void SyncRemotePickerWindow()
    {
        if (_vm?.RemotePathPickerViewModel is not { } pickerVm)
        {
            if (_remotePickerWindow is not null)
            {
                _remotePickerWindow.Close();
                _remotePickerWindow = null;
            }

            return;
        }

        if (_remotePickerWindow is not null)
            return;

        var window = new DirectoryPickerWindow(pickerVm);
        _remotePickerWindow = window;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_remotePickerWindow, window))
                _remotePickerWindow = null;
        };

        try
        {
            await window.ShowDialog<string?>(this);
        }
        catch
        {
            // ignored
        }
    }

    private async Task<string?> PickLocalFolderAsync()
    {
        if (StorageProvider is not { CanPickFolder: true })
            return null;

        var results = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select local folder",
        });

        if (results.Count == 0)
            return null;

        var path = results[0].Path;
        if (path.IsFile)
            return path.LocalPath;

        return Uri.UnescapeDataString(path.AbsolutePath);
    }
}

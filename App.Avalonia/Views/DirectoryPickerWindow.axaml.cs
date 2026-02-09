using System;
using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views;

public partial class DirectoryPickerWindow : Window
{
    private DirectoryPickerViewModel? _vm;

    public DirectoryPickerWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachViewModel();
        Closed += (_, _) => DetachViewModel();

        AttachViewModel();
    }

    public DirectoryPickerWindow(DirectoryPickerViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void AttachViewModel()
    {
        DetachViewModel();

        _vm = DataContext as DirectoryPickerViewModel;
        if (_vm is null)
            return;

        _vm.PathSelected += VmOnPathSelected;
        _vm.CloseRequested += VmOnCloseRequested;
    }

    private void DetachViewModel()
    {
        if (_vm is null)
            return;

        _vm.PathSelected -= VmOnPathSelected;
        _vm.CloseRequested -= VmOnCloseRequested;
        _vm = null;
    }

    private void VmOnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void VmOnPathSelected(object? sender, string? path)
    {
        // ShowDialog<T> will return the value passed to Close(T).
        Close(path);
    }
}

using System;
using System.ComponentModel;
using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;

namespace Coder.Desktop.App.Views;

public partial class SignInWindow : Window
{
    private readonly ContentControl _stageHost;

    private SignInViewModel? _vm;

    public SignInWindow()
    {
        InitializeComponent();

        _stageHost = this.FindControl<ContentControl>("StageHost")
                     ?? throw new InvalidOperationException("StageHost control was not found");

        DataContextChanged += (_, _) => AttachViewModel();
        Closed += (_, _) => DetachViewModel();

        AttachViewModel();
    }

    public SignInWindow(SignInViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void AttachViewModel()
    {
        DetachViewModel();

        _vm = DataContext as SignInViewModel;
        if (_vm is null)
        {
            // Placeholder content when no ViewModel is provided.
            _stageHost.Content = new SignInUrlPage();
            return;
        }

        _vm.PropertyChanged += VmOnPropertyChanged;
        _vm.CloseRequested += VmOnCloseRequested;

        UpdateStage();
    }

    private void DetachViewModel()
    {
        if (_vm is null)
            return;

        _vm.PropertyChanged -= VmOnPropertyChanged;
        _vm.CloseRequested -= VmOnCloseRequested;
        _vm = null;
    }

    private void VmOnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignInViewModel.Stage))
            UpdateStage();
    }

    private void UpdateStage()
    {
        if (_vm is null)
            return;

        var page = _vm.Stage switch
        {
            SignInStage.Url => (Control)new SignInUrlPage(),
            SignInStage.Token => new SignInTokenPage(),
            _ => new SignInUrlPage(),
        };

        page.DataContext = _vm;
        _stageHost.Content = page;
    }
}

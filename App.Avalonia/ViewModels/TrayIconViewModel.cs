using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public sealed class TrayIconViewModel : ObservableObject
{
    public IRelayCommand ShowWindowCommand { get; }
    public IRelayCommand ExitCommand { get; }

    public TrayIconViewModel(Action showOrToggleWindow, Action exit)
    {
        ShowWindowCommand = new RelayCommand(showOrToggleWindow);
        ExitCommand = new RelayCommand(exit);
    }
}

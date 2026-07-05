using CommunityToolkit.Mvvm.ComponentModel;

namespace Coder.Desktop.App.ViewModels;

public enum TrayWindowShellPage
{
    Loading,
    Disconnected,
    LoginRequired,
    Main,
}

/// <summary>
/// A light-weight shell ViewModel for the TrayWindow.
///
/// The WinUI implementation performed page switching in code-behind based on
/// RPC + credential state. For Avalonia we keep the shell state in a VM so the
/// view can swap page content via a ContentControl.
/// </summary>
public sealed partial class TrayWindowShellViewModel : ObservableObject
{
    [ObservableProperty]
    private TrayWindowShellPage _page = TrayWindowShellPage.Loading;
}

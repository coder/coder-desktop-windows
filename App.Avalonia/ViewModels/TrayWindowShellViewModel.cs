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
    public partial TrayWindowShellPage Page { get; set; } = TrayWindowShellPage.Loading;

    /// <summary>
    /// Height of the content host. We animate this value via Avalonia
    /// transitions to approximate the WinUI height storyboard behavior.
    /// </summary>
    [ObservableProperty]
    public partial double ContentHeight { get; set; } = 240;

    partial void OnPageChanged(TrayWindowShellPage value)
    {
        // These are placeholder sizes until real pages land.
        ContentHeight = value switch
        {
            TrayWindowShellPage.Loading => 160,
            TrayWindowShellPage.Disconnected => 220,
            TrayWindowShellPage.LoginRequired => 240,
            TrayWindowShellPage.Main => 420,
            _ => 240,
        };
    }
}

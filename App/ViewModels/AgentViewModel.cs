using Windows.ApplicationModel.DataTransfer;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Coder.Desktop.App.ViewModels;

public enum AgentConnectionStatus
{
    Green,
    Red,
    Yellow,
    Gray,
}

public partial class AgentViewModel
{
    public required string Hostname { get; set; }

    public required string HostnameSuffix { get; set; } // including leading dot

    public required AgentConnectionStatus ConnectionStatus { get; set; }

    public string FullHostname => Hostname + HostnameSuffix;

    public required string DashboardUrl { get; set; }

    [RelayCommand]
    private void CopyHostname(object parameter)
    {
        var dataPackage = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy,
        };
        dataPackage.SetText(FullHostname);
        Clipboard.SetContent(dataPackage);

        if (parameter is not FrameworkElement frameworkElement) return;

        var flyout = new Flyout
        {
            Content = new TextBlock
            {
                Text = "DNS Copied",
                Margin = new Thickness(4),
            },
        };
        FlyoutBase.SetAttachedFlyout(frameworkElement, flyout);
        FlyoutBase.ShowAttachedFlyout(frameworkElement);
    }
}

using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Coder.Desktop.App.Models;

public enum AgentConnectionStatus
{
    Green,
    Red,
    Gray,
}

public partial class AgentModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullHostname))]
    public required partial string Hostname { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullHostname))]
    public required partial string HostnameSuffix { get; set; } // including leading dot

    public string FullHostname => Hostname + HostnameSuffix;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusColor))]
    public required partial AgentConnectionStatus ConnectionStatus { get; set; }

    public Brush ConnectionStatusColor => ConnectionStatus switch
    {
        AgentConnectionStatus.Green => new SolidColorBrush(Color.FromArgb(255, 52, 199, 89)),
        AgentConnectionStatus.Red => new SolidColorBrush(Color.FromArgb(255, 255, 59, 48)),
        _ => new SolidColorBrush(Color.FromArgb(255, 142, 142, 147)),
    };

    [ObservableProperty]
    public required partial string DashboardUrl { get; set; }

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

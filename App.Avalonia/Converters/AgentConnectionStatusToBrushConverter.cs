using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Converters;

public class AgentConnectionStatusToBrushConverter : IValueConverter
{
    private static readonly IBrush HealthyBrush = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#FFCC01"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF3B30"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#8E8E93"));

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not AgentConnectionStatus status)
            return OfflineBrush;

        return status switch
        {
            AgentConnectionStatus.Healthy => HealthyBrush,
            AgentConnectionStatus.Connecting => WarningBrush,
            AgentConnectionStatus.Unhealthy => WarningBrush,
            AgentConnectionStatus.NoRecentHandshake => ErrorBrush,
            AgentConnectionStatus.Offline => OfflineBrush,
            _ => OfflineBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

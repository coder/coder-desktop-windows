using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Coder.Desktop.App.Models;

namespace Coder.Desktop.App.Converters;

public sealed class VpnLifecycleToBoolConverter : IValueConverter
{
    public bool Unknown { get; set; } = false;

    public bool Starting { get; set; } = false;

    public bool Started { get; set; } = false;

    public bool Stopping { get; set; } = false;

    public bool Stopped { get; set; } = false;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not VpnLifecycle lifecycle)
        {
            return Stopped;
        }

        return lifecycle switch
        {
            VpnLifecycle.Unknown => Unknown,
            VpnLifecycle.Starting => Starting,
            VpnLifecycle.Started => Started,
            VpnLifecycle.Stopping => Stopping,
            VpnLifecycle.Stopped => Stopped,
            _ => false,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Coder.Desktop.App.Converters;

public sealed class BoolToObjectConverter : IValueConverter
{
    public object? TrueValue { get; set; } = true;

    public object? FalseValue { get; set; } = true;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? TrueValue : FalseValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

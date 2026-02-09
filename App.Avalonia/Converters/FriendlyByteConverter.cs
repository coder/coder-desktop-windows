extern alias AppShared;

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SharedFriendlyByteConverter = AppShared::Coder.Desktop.App.Converters.FriendlyByteConverter;

namespace Coder.Desktop.App.Converters;

public sealed class FriendlyByteConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            int i => i < 0 ? 0ul : (ulong)i,
            uint ui => ui,
            long l => l < 0 ? 0ul : (ulong)l,
            ulong ul => ul,
            _ => 0ul,
        };

        return SharedFriendlyByteConverter.FriendlyBytes(bytes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

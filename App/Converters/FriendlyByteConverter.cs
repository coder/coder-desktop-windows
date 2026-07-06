using System;
using Microsoft.UI.Xaml.Data;

namespace Coder.Desktop.App.Converters;

public class FriendlyByteConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        switch (value)
        {
            case int i:
                if (i < 0) i = 0;
                return FriendlyBytes((ulong)i);
            case uint ui:
                return FriendlyBytes(ui);
            case long l:
                if (l < 0) l = 0;
                return FriendlyBytes((ulong)l);
            case ulong ul:
                return FriendlyBytes(ul);
            default:
                return FriendlyBytes(0);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    public static string FriendlyBytes(ulong bytes)
    {
        return Utils.FriendlyByteConverter.FriendlyBytes(bytes);
    }
}

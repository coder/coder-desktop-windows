using System;
using Windows.UI;
using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Coder.Desktop.App.Converters;

public class AgentStatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromArgb(255, 52, 199, 89));
    private static readonly SolidColorBrush Red = new(Color.FromArgb(255, 255, 59, 48));
    private static readonly SolidColorBrush Gray = new(Color.FromArgb(255, 142, 142, 147));
    private static readonly SolidColorBrush Yellow = new(Color.FromArgb(255, 204, 1, 0));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not AgentConnectionStatus status) return Gray;

        return status switch
        {
            AgentConnectionStatus.Green => Green,
            AgentConnectionStatus.Yellow => Yellow,
            AgentConnectionStatus.Red => Red,
            _ => Gray,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

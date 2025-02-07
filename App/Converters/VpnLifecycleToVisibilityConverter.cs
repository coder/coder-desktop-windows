using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Coder.Desktop.App.Converters;

public partial class VpnLifecycleToVisibilityConverter : VpnLifecycleToBoolConverter, IValueConverter
{
    public new object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = base.Convert(value, targetType, parameter, language);
        return boolValue is true ? Visibility.Visible : Visibility.Collapsed;
    }
}

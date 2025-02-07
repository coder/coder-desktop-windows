using System;
using Coder.Desktop.App.Models;
using DependencyPropertyGenerator;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Coder.Desktop.App.Converters;

[DependencyProperty<bool>("Starting", DefaultValue = false)]
[DependencyProperty<bool>("Started", DefaultValue = false)]
[DependencyProperty<bool>("Stopping", DefaultValue = false)]
[DependencyProperty<bool>("Stopped", DefaultValue = false)]
public partial class VpnLifecycleToBoolConverter : DependencyObject, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not VpnLifecycle lifecycle) return Stopped;

        return lifecycle switch
        {
            VpnLifecycle.Starting => Starting,
            VpnLifecycle.Started => Started,
            VpnLifecycle.Stopping => Stopping,
            VpnLifecycle.Stopped => Stopped,
            _ => Visibility.Collapsed,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

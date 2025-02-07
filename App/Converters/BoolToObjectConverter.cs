using System;
using DependencyPropertyGenerator;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Coder.Desktop.App.Converters;

[DependencyProperty<object>("TrueValue", DefaultValue = true)]
[DependencyProperty<object>("FalseValue", DefaultValue = true)]
public partial class BoolToObjectConverter : DependencyObject, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? TrueValue : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

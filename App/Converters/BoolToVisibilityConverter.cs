using Microsoft.UI.Xaml;

namespace Coder.Desktop.App.Converters;

public partial class BoolToVisibilityConverter : BoolToObjectConverter
{
    public BoolToVisibilityConverter()
    {
        TrueValue = Visibility.Visible;
        FalseValue = Visibility.Collapsed;
    }
}

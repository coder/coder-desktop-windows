using Microsoft.UI.Xaml;

namespace Coder.Desktop.App.Converters;

public partial class InverseBoolToVisibilityConverter : BoolToObjectConverter
{
    public InverseBoolToVisibilityConverter()
    {
        TrueValue = Visibility.Collapsed;
        FalseValue = Visibility.Visible;
    }
}

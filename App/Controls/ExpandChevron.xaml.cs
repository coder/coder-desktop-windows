using DependencyPropertyGenerator;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Coder.Desktop.App.Controls;

[DependencyProperty<bool>("IsOpen", DefaultValue = false)]
[DependencyProperty<SolidColorBrush>("Foreground")]
public sealed partial class ExpandChevron : UserControl
{
    public ExpandChevron()
    {
        InitializeComponent();
    }

    partial void OnIsOpenChanged(bool oldValue, bool newValue)
    {
        var newState = newValue ? "NormalOn" : "NormalOff";
        AnimatedIcon.SetState(ChevronIcon, newState);
    }
}

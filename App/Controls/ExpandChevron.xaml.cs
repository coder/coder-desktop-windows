using DependencyPropertyGenerator;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Controls;

[DependencyProperty<bool>("IsOpen", DefaultValue = false)]
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

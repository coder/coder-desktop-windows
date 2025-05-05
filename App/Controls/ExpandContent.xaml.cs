using DependencyPropertyGenerator;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Coder.Desktop.App.Controls;

[ContentProperty(Name = nameof(Children))]
[DependencyProperty<bool>("IsOpen", DefaultValue = false)]
public sealed partial class ExpandContent : UserControl
{
    public UIElementCollection Children => CollapsiblePanel.Children;

    public ExpandContent()
    {
        InitializeComponent();
    }

    public void CollapseAnimation_Completed(object? sender, object args)
    {
        // Hide the panel completely when the collapse animation is done. This
        // cannot be done with keyframes for some reason.
        //
        // Without this, the space will still be reserved for the panel.
        CollapsiblePanel.Visibility = Visibility.Collapsed;
    }

    partial void OnIsOpenChanged(bool oldValue, bool newValue)
    {
        var newState = newValue ? "ExpandedState" : "CollapsedState";

        // The animation can't set visibility when starting or ending the
        // animation.
        if (newValue)
            CollapsiblePanel.Visibility = Visibility.Visible;

        VisualStateManager.GoToState(this, newState, true);
    }
}

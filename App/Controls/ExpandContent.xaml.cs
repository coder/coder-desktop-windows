using DependencyPropertyGenerator;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Threading.Tasks;

namespace Coder.Desktop.App.Controls;


[ContentProperty(Name = nameof(Children))]
[DependencyProperty<bool>("IsOpen", DefaultValue = false)]
public sealed partial class ExpandContent : UserControl
{
    public UIElementCollection Children => CollapsiblePanel.Children;

    private readonly string _expandedState = "ExpandedState";
    private readonly string _collapsedState = "CollapsedState";

    public ExpandContent()
    {
        InitializeComponent();
        Loaded += (_, __) =>
        {
            // When we load the control for the first time (after panel swapping)
            // we need to set the initial state based on IsOpen.
            VisualStateManager.GoToState(
                this,
                IsOpen ? _expandedState : _collapsedState,
                useTransitions: false);   // NO animation yet

            // If IsOpen was already true we must also show the panel
            if (IsOpen)
            {
                CollapsiblePanel.Visibility = Visibility.Visible;
                // This makes the panel expand to its full height
                CollapsiblePanel.ClearValue(FrameworkElement.MaxHeightProperty);
            }
        };
    }

    partial void OnIsOpenChanged(bool oldValue, bool newValue)
    {
        var newState = newValue ? _expandedState : _collapsedState;
        if (newValue)
        {
            CollapsiblePanel.Visibility = Visibility.Visible;
            // We use BeginTime to ensure other panels are collapsed first.
            // If the user clicks the expand button quickly, we want to avoid
            // the panel expanding to its full height before the collapse animation completes.
            CollapseSb.SkipToFill();
        }

        VisualStateManager.GoToState(this, newState, true);
    }

    private void CollapseStoryboard_Completed(object sender, object e)
    {
        CollapsiblePanel.Visibility = Visibility.Collapsed;
    }
}

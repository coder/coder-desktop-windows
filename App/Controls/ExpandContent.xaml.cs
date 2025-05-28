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

    private bool? _pendingIsOpen;


    public ExpandContent()
    {
        InitializeComponent();
        Loaded += (_, __) =>
        {
            // When we load the control for the first time (after panel swapping)
            // we need to set the initial state based on IsOpen.
            VisualStateManager.GoToState(
                this,
                IsOpen ? "ExpandedState" : "CollapsedState",
                useTransitions: false);   // ← NO animation yet

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
        if (!IsLoaded)
        {
            _pendingIsOpen = newValue;
            return;
        }
        _ = AnimateAsync(newValue);
    }

    private async Task AnimateAsync(bool open)
    {
        if (open)
        {
            if (_currentlyOpen is not null && _currentlyOpen != this)
                await _currentlyOpen.StartCollapseAsync();

            _currentlyOpen = this;
            CollapsiblePanel.Visibility = Visibility.Visible;

            VisualStateManager.GoToState(this, "ExpandedState", true);
            await ExpandAsync();
        }
        else
        {
            if (_currentlyOpen == this) _currentlyOpen = null;
            await StartCollapseAsync();
        }
    }

    private static ExpandContent? _currentlyOpen;
    private TaskCompletionSource? _collapseTcs;

    private async Task ExpandAsync()
    {
        CollapsiblePanel.Visibility = Visibility.Visible;
        VisualStateManager.GoToState(this, "ExpandedState", true);

        var tcs = new TaskCompletionSource();
        void done(object? s, object e) { ExpandSb.Completed -= done; tcs.SetResult(); }
        ExpandSb.Completed += done;
        await tcs.Task;
    }

    private Task StartCollapseAsync()
    {
        _collapseTcs = new TaskCompletionSource();
        VisualStateManager.GoToState(this, "CollapsedState", true);
        return _collapseTcs.Task;
    }

    private void CollapseStoryboard_Completed(object sender, object e)
    {
        CollapsiblePanel.Visibility = Visibility.Collapsed;
        _collapseTcs?.TrySetResult();
        _collapseTcs = null;
    }
}

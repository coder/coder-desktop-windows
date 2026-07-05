using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Metadata;

namespace Coder.Desktop.App.Controls;

public partial class ExpandContent : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ExpandContent, bool>(nameof(IsOpen));

    static ExpandContent()
    {
        IsOpenProperty.Changed.AddClassHandler<ExpandContent>((x, e) =>
        {
            if (e.NewValue is bool isOpen)
            {
                x.SetOpenState(isOpen, immediate: false);
            }
        });
    }

    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(160);

    private CancellationTokenSource? _collapseCts;

    private TranslateTransform? _slideTransform;

    // During XAML population, this content property can be accessed before the
    // named CollapsiblePanel field is assigned, so stage children here first.
    private readonly List<Control> _deferredChildren = [];

    [Content]
    public IList<Control> Children
    {
        get
        {
            if (CollapsiblePanel is null)
                return _deferredChildren;

            if (_deferredChildren.Count > 0)
            {
                foreach (var child in _deferredChildren)
                    CollapsiblePanel.Children.Add(child);

                _deferredChildren.Clear();
            }

            return CollapsiblePanel.Children;
        }
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ExpandContent()
    {
        InitializeComponent();

        // Flush any content that may have been staged before named controls
        // were available.
        _ = Children;

        _slideTransform = CollapsiblePanel.RenderTransform as TranslateTransform;

        SetOpenState(IsOpen, immediate: true);
    }

    private void SetOpenState(bool isOpen, bool immediate)
    {
        if (CollapsiblePanel is null || _slideTransform is null)
        {
            return;
        }

        if (isOpen)
        {
            _collapseCts?.Cancel();
            CollapsiblePanel.IsVisible = true;

            CollapsiblePanel.Opacity = 1;
            CollapsiblePanel.MaxHeight = 10000;
            _slideTransform.Y = 0;
            return;
        }

        CollapsiblePanel.Opacity = 0;
        CollapsiblePanel.MaxHeight = 0;
        _slideTransform.Y = -16;

        if (immediate)
        {
            CollapsiblePanel.IsVisible = false;
            return;
        }

        ScheduleHideAfterCollapse();
    }

    private async void ScheduleHideAfterCollapse()
    {
        _collapseCts?.Cancel();
        _collapseCts = new CancellationTokenSource();
        var token = _collapseCts.Token;

        try
        {
            await Task.Delay(TransitionDuration, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested && !IsOpen)
        {
            CollapsiblePanel.IsVisible = false;
        }
    }
}

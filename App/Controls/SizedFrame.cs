using System;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Controls;

public class SizedFrameEventArgs : EventArgs
{
    public Size NewSize { get; init; }
}

/// <summary>
///     SizedFrame extends Frame by adding a SizeChanged event, which will be triggered when:
///     - The contained Page's content's size changes
///     - We switch to a different page.
///     Sadly this is necessary because Window.Content.SizeChanged doesn't trigger when the Page's content changes.
/// </summary>
public class SizedFrame : Frame
{
    public delegate void SizeChangeDelegate(object sender, SizedFrameEventArgs e);

    public new event SizeChangeDelegate? SizeChanged;

    private Size _lastSize;

    public void SetPage(Page page)
    {
        if (ReferenceEquals(page, Content)) return;

        // Set the new event listener.
        if (page.Content is not FrameworkElement newElement)
            throw new Exception("Failed to get Page.Content as FrameworkElement on SizedFrame navigation");
        newElement.SizeChanged += Content_SizeChanged;

        // Unset the previous event listener.
        if (Content is Page { Content: FrameworkElement oldElement })
            oldElement.SizeChanged -= Content_SizeChanged;

        // We don't use RootFrame.Navigate here because it doesn't let you
        // instantiate the page yourself. We also don't need forwards/backwards
        // capabilities.
        Content = page;

        // Fire an event.
        Content_SizeChanged(newElement, null);
    }

    public Size GetContentSize()
    {
        if (Content is not Page { Content: FrameworkElement frameworkElement })
            throw new Exception("Failed to get Content as FrameworkElement for SizedFrame");

        frameworkElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(frameworkElement.ActualWidth, frameworkElement.ActualHeight);
    }

    private void Content_SizeChanged(object sender, SizeChangedEventArgs? _)
    {
        var size = GetContentSize();
        if (size == _lastSize) return;
        _lastSize = size;

        var args = new SizedFrameEventArgs { NewSize = size };
        SizeChanged?.Invoke(this, args);
    }
}

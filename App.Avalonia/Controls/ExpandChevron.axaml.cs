using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Coder.Desktop.App.Controls;

public partial class ExpandChevron : UserControl
{
    private static readonly Geometry RightChevron = Geometry.Parse("M 5,3 L 11,8 L 5,13 L 6.4,14.4 L 13,8 L 6.4,1.6 Z");
    private static readonly Geometry DownChevron = Geometry.Parse("M 3,5 L 8,11 L 13,5 L 14.4,6.4 L 8,13 L 1.6,6.4 Z");

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ExpandChevron, bool>(nameof(IsOpen));

    static ExpandChevron()
    {
        IsOpenProperty.Changed.AddClassHandler<ExpandChevron>((x, e) =>
        {
            if (e.NewValue is bool isOpen)
            {
                x.UpdateChevronGlyph(isOpen);
            }
        });
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ExpandChevron()
    {
        InitializeComponent();

        UpdateChevronGlyph(IsOpen);
    }

    private void UpdateChevronGlyph(bool isOpen)
    {
        if (ChevronIcon is null)
        {
            return;
        }

        ChevronIcon.Data = isOpen ? DownChevron : RightChevron;
    }
}

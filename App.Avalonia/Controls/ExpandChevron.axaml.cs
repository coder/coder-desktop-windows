using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Coder.Desktop.App.Controls;

public partial class ExpandChevron : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ExpandChevron, bool>(nameof(IsOpen));

    static ExpandChevron()
    {
        IsOpenProperty.Changed.AddClassHandler<ExpandChevron>((x, e) =>
        {
            if (e.NewValue is bool isOpen)
            {
                x.UpdateChevronAngle(isOpen);
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

        UpdateChevronAngle(IsOpen);
    }

    private void UpdateChevronAngle(bool isOpen)
    {
        if (ChevronIcon is null)
        {
            return;
        }

        if (ChevronIcon.RenderTransform is RotateTransform rotate)
        {
            rotate.Angle = isOpen ? 90 : 0;
        }
    }
}

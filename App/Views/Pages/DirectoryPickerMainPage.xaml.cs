using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class DirectoryPickerMainPage : Page
{
    public readonly DirectoryPickerViewModel ViewModel;

    public DirectoryPickerMainPage(DirectoryPickerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void TooltipText_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs e)
    {
        ToolTipService.SetToolTip(sender, null);
        if (!sender.IsTextTrimmed) return;

        var toolTip = new ToolTip
        {
            Content = sender.Text,
        };
        ToolTipService.SetToolTip(sender, toolTip);
    }
}

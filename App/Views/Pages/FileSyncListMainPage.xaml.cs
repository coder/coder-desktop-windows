using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class FileSyncListMainPage : Page
{
    public FileSyncListViewModel ViewModel;

    public FileSyncListMainPage(FileSyncListViewModel viewModel)
    {
        ViewModel = viewModel; // already initialized
        InitializeComponent();
    }

    // Adds a tooltip with the full text when it's ellipsized.
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

using Coder.Desktop.App.Models;
using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml;
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

    // Check the comments in FileSyncListMainPage.xaml to see why this tooltip
    // stuff is necessary.
    private static void SetToolTip(FrameworkElement element, string text)
    {
        // Get current tooltip and compare the text. Setting the tooltip with
        // the same text causes it to dismiss itself.
        var currentToolTip = ToolTipService.GetToolTip(element) as ToolTip;
        if (currentToolTip?.Content as string == text) return;

        ToolTipService.SetToolTip(element, new ToolTip { Content = text });
    }

    private void StatusText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SyncSessionModel model } element)
            SetToolTip(element, model.StatusDetails);
    }

    private void StatusText_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender.DataContext is SyncSessionModel model)
            SetToolTip(sender, model.StatusDetails);
    }

    private void SizeText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SyncSessionModel model } element)
            SetToolTip(element, model.SizeDetails);
    }

    private void SizeText_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender.DataContext is SyncSessionModel model)
            SetToolTip(sender, model.SizeDetails);
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

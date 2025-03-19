using System.Threading.Tasks;
using Coder.Desktop.App.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class FileSyncListMainPage : Page
{
    public FileSyncListViewModel ViewModel;

    private readonly Window _window;

    public FileSyncListMainPage(FileSyncListViewModel viewModel, Window window)
    {
        ViewModel = viewModel; // already initialized
        _window = window;
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

    [RelayCommand]
    public async Task OpenLocalPathSelectDialog()
    {
        await ViewModel.OpenLocalPathSelectDialog(_window);
    }
}

using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class FileSyncListMainPage : UserControl
{
    public FileSyncListViewModel? ViewModel { get; private set; }

    public FileSyncListMainPage()
    {
        InitializeComponent();
    }

    public FileSyncListMainPage(FileSyncListViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }
}

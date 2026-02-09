using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class DirectoryPickerMainPage : UserControl
{
    public DirectoryPickerViewModel? ViewModel { get; private set; }

    public DirectoryPickerMainPage()
    {
        InitializeComponent();
    }

    public DirectoryPickerMainPage(DirectoryPickerViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }
}

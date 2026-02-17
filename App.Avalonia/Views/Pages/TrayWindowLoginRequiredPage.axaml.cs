using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class TrayWindowLoginRequiredPage : UserControl
{
    public TrayWindowLoginRequiredViewModel? ViewModel { get; private set; }

    public TrayWindowLoginRequiredPage()
    {
        InitializeComponent();
    }

    public TrayWindowLoginRequiredPage(TrayWindowLoginRequiredViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }
}

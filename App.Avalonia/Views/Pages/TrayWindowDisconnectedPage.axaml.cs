using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class TrayWindowDisconnectedPage : UserControl
{
    public TrayWindowDisconnectedViewModel? ViewModel { get; private set; }

    public TrayWindowDisconnectedPage()
    {
        InitializeComponent();
    }

    public TrayWindowDisconnectedPage(TrayWindowDisconnectedViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }
}

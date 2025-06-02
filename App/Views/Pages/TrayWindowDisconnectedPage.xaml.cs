using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class TrayWindowDisconnectedPage : Page
{
    public TrayWindowDisconnectedViewModel ViewModel { get; }

    public TrayWindowDisconnectedPage(TrayWindowDisconnectedViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
    }
}

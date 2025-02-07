using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class TrayWindowLoginRequiredPage : Page
{
    public TrayWindowLoginRequiredViewModel ViewModel { get; }

    public TrayWindowLoginRequiredPage(TrayWindowLoginRequiredViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
    }
}

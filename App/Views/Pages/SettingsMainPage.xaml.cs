using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class SettingsMainPage : Page
{
    public SettingsViewModel ViewModel;

    public SettingsMainPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}

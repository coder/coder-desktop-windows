using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class SettingsMainPage : UserControl
{
    public SettingsViewModel? ViewModel { get; private set; }

    public SettingsMainPage()
    {
        InitializeComponent();
    }

    public SettingsMainPage(SettingsViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }
}

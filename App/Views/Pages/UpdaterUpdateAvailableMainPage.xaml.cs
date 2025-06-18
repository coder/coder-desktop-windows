using Microsoft.UI.Xaml.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class UpdaterUpdateAvailableMainPage : Page
{
    public readonly UpdaterUpdateAvailableViewModel ViewModel;

    public UpdaterUpdateAvailableMainPage(UpdaterUpdateAvailableViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}

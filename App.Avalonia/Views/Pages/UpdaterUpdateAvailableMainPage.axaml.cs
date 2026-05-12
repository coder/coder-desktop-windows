using Avalonia.Controls;
using Avalonia.Interactivity;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class UpdaterUpdateAvailableMainPage : UserControl
{
    public UpdaterUpdateAvailableViewModel? ViewModel { get; private set; }

    public UpdaterUpdateAvailableMainPage()
    {
        InitializeComponent();
    }

    public UpdaterUpdateAvailableMainPage(UpdaterUpdateAvailableViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void SkipButton_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SkipButton_Click();
    }

    private void RemindMeLaterButton_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.RemindMeLaterButton_Click();
    }

    private void InstallButton_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.InstallButton_Click();
    }
}

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class UpdaterDownloadProgressMainPage : UserControl
{
    public UpdaterDownloadProgressViewModel? ViewModel { get; private set; }

    public UpdaterDownloadProgressMainPage()
    {
        InitializeComponent();
    }

    public UpdaterDownloadProgressMainPage(UpdaterDownloadProgressViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void ActionButton_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ActionButton_Click(sender, EventArgs.Empty);
    }
}

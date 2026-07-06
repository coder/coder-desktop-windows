using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class UpdaterDownloadProgressMainPage : Page
{
    public readonly UpdaterDownloadProgressViewModel ViewModel;
    public UpdaterDownloadProgressMainPage(UpdaterDownloadProgressViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActionButton_Click(sender, EventArgs.Empty);
    }
}

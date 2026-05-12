using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App.Views.Pages;

public partial class TrayWindowMainPage : UserControl
{
    public TrayWindowViewModel? ViewModel { get; private set; }

    public TrayWindowMainPage()
    {
        InitializeComponent();
    }

    public TrayWindowMainPage(TrayWindowViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void CreateWorkspaceButton_Click(object? sender, RoutedEventArgs e)
    {
        var url = ViewModel?.DashboardUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignored
        }
    }
}

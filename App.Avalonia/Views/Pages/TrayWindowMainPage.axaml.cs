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

        DataContextChanged += (_, _) =>
        {
            ViewModel = DataContext as TrayWindowViewModel;
        };
    }

    public TrayWindowMainPage(TrayWindowViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void CreateWorkspaceButton_Click(object? sender, RoutedEventArgs e)
    {
        LaunchUrl(ViewModel?.DashboardUrl);
    }

    private void OpenWorkspaceButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AgentViewModel agentVm })
            return;

        LaunchUrl(agentVm.DashboardUrl);
    }

    private static void LaunchUrl(string? url)
    {
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

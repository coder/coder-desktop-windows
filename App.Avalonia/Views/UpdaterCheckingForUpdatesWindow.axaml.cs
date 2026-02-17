using System;
using Avalonia.Controls;
using NetSparkleUpdater.Interfaces;

namespace Coder.Desktop.App.Views;

public partial class UpdaterCheckingForUpdatesWindow : Window, ICheckingForUpdates
{
    // Implements ICheckingForUpdates
    public event EventHandler? UpdatesUIClosing;

    public UpdaterCheckingForUpdatesWindow()
    {
        InitializeComponent();

        Closed += (_, _) => UpdatesUIClosing?.Invoke(this, EventArgs.Empty);
    }

    void ICheckingForUpdates.Show()
    {
        Show();
        Activate();
    }

    void ICheckingForUpdates.Close()
    {
        Close();
    }
}

using Microsoft.UI.Xaml.Media;
using System;
using Coder.Desktop.App.Utils;
using NetSparkleUpdater.Interfaces;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class UpdaterCheckingForUpdatesWindow : WindowEx, ICheckingForUpdates
{
    // Implements ICheckingForUpdates
    public event EventHandler? UpdatesUIClosing;

    public UpdaterCheckingForUpdatesWindow()
    {
        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.Hide();

        Closed += (_, _) => UpdatesUIClosing?.Invoke(this, EventArgs.Empty);
    }

    void ICheckingForUpdates.Show()
    {
        AppWindow.Show();
        this.CenterOnScreen();
    }

    void ICheckingForUpdates.Close()
    {
        Close();
    }
}

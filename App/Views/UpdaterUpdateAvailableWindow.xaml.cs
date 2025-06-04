using Microsoft.UI.Xaml.Media;
using Coder.Desktop.App.Utils;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Xaml;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class UpdaterUpdateAvailableWindow : WindowEx, IUpdateAvailable
{
    public readonly UpdaterUpdateAvailableViewModel ViewModel;

    // Implements IUpdateAvailable
    public UpdateAvailableResult Result => ViewModel.Result;
    // Implements IUpdateAvailable
    public AppCastItem CurrentItem => ViewModel.CurrentItem;
    // Implements IUpdateAvailable
    public event UserRespondedToUpdate? UserResponded;

    private bool _respondedToUpdate;

    public UpdaterUpdateAvailableWindow(UpdaterUpdateAvailableViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.UserResponded += (_, args) =>
            UserRespondedToUpdateCheck(args.Result);

        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.Hide();

        RootFrame.Content = new UpdaterUpdateAvailableMainPage(ViewModel);

        Closed += UpdaterUpdateAvailableWindow_Closed;
    }

    private void UpdaterUpdateAvailableWindow_Closed(object sender, WindowEventArgs args)
    {
        UserRespondedToUpdateCheck(UpdateAvailableResult.None);
    }

    void IUpdateAvailable.Show()
    {
        AppWindow.Show();
        this.CenterOnScreen();
    }

    void IUpdateAvailable.Close()
    {
        UserRespondedToUpdateCheck(UpdateAvailableResult.None); // the Avalonia UI does this "just in case"
        Close();
    }

    void IUpdateAvailable.HideReleaseNotes()
    {
        ViewModel.HideReleaseNotes();
    }

    void IUpdateAvailable.HideRemindMeLaterButton()
    {
        ViewModel.HideRemindMeLaterButton();
    }

    void IUpdateAvailable.HideSkipButton()
    {
        ViewModel.HideSkipButton();
    }

    void IUpdateAvailable.BringToFront()
    {
        Activate();
        ForegroundWindow.MakeForeground(this);
    }

    private void UserRespondedToUpdateCheck(UpdateAvailableResult response)
    {
        if (_respondedToUpdate)
            return;
        _respondedToUpdate = true;
        UserResponded?.Invoke(this, new UpdateResponseEventArgs(response, CurrentItem));
        // Prevent further interaction.
        Close();
    }
}

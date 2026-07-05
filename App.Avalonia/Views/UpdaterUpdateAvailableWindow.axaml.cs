using System;
using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace Coder.Desktop.App.Views;

public partial class UpdaterUpdateAvailableWindow : Window, IUpdateAvailable
{
    public UpdaterUpdateAvailableViewModel? ViewModel { get; private set; }

    // Implements IUpdateAvailable
    public UpdateAvailableResult Result => ViewModel?.Result ?? UpdateAvailableResult.None;

    // Implements IUpdateAvailable
    public AppCastItem CurrentItem =>
        ViewModel?.CurrentItem ?? throw new InvalidOperationException("UpdaterUpdateAvailableWindow has no ViewModel");

    // Implements IUpdateAvailable
    public event UserRespondedToUpdate? UserResponded;

    private bool _respondedToUpdate;

    public UpdaterUpdateAvailableWindow()
    {
        InitializeComponent();
    }

    public UpdaterUpdateAvailableWindow(UpdaterUpdateAvailableViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.UserResponded += (_, args) => UserRespondedToUpdateCheck(args.Result);
        Closed += (_, _) => UserRespondedToUpdateCheck(UpdateAvailableResult.None);
    }

    void IUpdateAvailable.Show()
    {
        Show();
        Activate();
    }

    void IUpdateAvailable.Close()
    {
        // NetSparkle recommends sending a response "just in case".
        UserRespondedToUpdateCheck(UpdateAvailableResult.None);
        Close();
    }

    void IUpdateAvailable.HideReleaseNotes()
    {
        ViewModel?.HideReleaseNotes();
    }

    void IUpdateAvailable.HideRemindMeLaterButton()
    {
        ViewModel?.HideRemindMeLaterButton();
    }

    void IUpdateAvailable.HideSkipButton()
    {
        ViewModel?.HideSkipButton();
    }

    void IUpdateAvailable.BringToFront()
    {
        Activate();
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

using System;
using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace Coder.Desktop.App.Views;

public partial class UpdaterDownloadProgressWindow : Window, IDownloadProgress
{
    // Implements IDownloadProgress
    public event DownloadInstallEventHandler? DownloadProcessCompleted;

    public UpdaterDownloadProgressViewModel? ViewModel { get; }

    private bool _downloadProcessCompletedInvoked;

    public UpdaterDownloadProgressWindow()
    {
        InitializeComponent();

        Closed += (_, _) => SendResponse(new DownloadInstallEventArgs(false));
    }

    public UpdaterDownloadProgressWindow(UpdaterDownloadProgressViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.DownloadProcessCompleted += (_, args) => SendResponse(args);
    }

    public void SendResponse(DownloadInstallEventArgs args)
    {
        if (_downloadProcessCompletedInvoked)
            return;
        _downloadProcessCompletedInvoked = true;
        DownloadProcessCompleted?.Invoke(this, args);
    }

    void IDownloadProgress.SetDownloadAndInstallButtonEnabled(bool shouldBeEnabled)
    {
        ViewModel?.SetActionButtonEnabled(shouldBeEnabled);
    }

    void IDownloadProgress.Show()
    {
        Show();
        Activate();
    }

    void IDownloadProgress.Close()
    {
        Close();
    }

    void IDownloadProgress.OnDownloadProgressChanged(object sender, ItemDownloadProgressEventArgs args)
    {
        ViewModel?.SetDownloadProgress((ulong)args.BytesReceived, (ulong)args.TotalBytesToReceive);
    }

    void IDownloadProgress.FinishedDownloadingFile(bool isDownloadedFileValid)
    {
        ViewModel?.SetFinishedDownloading(isDownloadedFileValid);
    }

    bool IDownloadProgress.DisplayErrorMessage(string errorMessage)
    {
        _ = new MessageWindow("Download failed", errorMessage, "Coder Desktop Updater");
        Close();
        return true;
    }
}

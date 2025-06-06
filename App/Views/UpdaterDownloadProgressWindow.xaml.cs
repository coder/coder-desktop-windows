using Microsoft.UI.Xaml.Media;
using Coder.Desktop.App.Utils;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;
using WinUIEx;
using WindowEventArgs = Microsoft.UI.Xaml.WindowEventArgs;

namespace Coder.Desktop.App.Views;

public sealed partial class UpdaterDownloadProgressWindow : WindowEx, IDownloadProgress
{
    // Implements IDownloadProgress
    public event DownloadInstallEventHandler? DownloadProcessCompleted;

    public UpdaterDownloadProgressViewModel ViewModel;

    private bool _didCallDownloadProcessCompletedHandler;

    public UpdaterDownloadProgressWindow(UpdaterDownloadProgressViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.DownloadProcessCompleted += (_, args) => SendResponse(args);

        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.Hide();

        RootFrame.Content = new UpdaterDownloadProgressMainPage(ViewModel);

        Closed += UpdaterDownloadProgressWindow_Closed;
    }

    public void SendResponse(DownloadInstallEventArgs args)
    {
        if (_didCallDownloadProcessCompletedHandler)
            return;
        _didCallDownloadProcessCompletedHandler = true;
        DownloadProcessCompleted?.Invoke(this, args);
    }

    private void UpdaterDownloadProgressWindow_Closed(object sender, WindowEventArgs args)
    {
        SendResponse(new DownloadInstallEventArgs(false)); // Cancel
    }

    void IDownloadProgress.SetDownloadAndInstallButtonEnabled(bool shouldBeEnabled)
    {
        ViewModel.SetActionButtonEnabled(shouldBeEnabled);
    }

    void IDownloadProgress.Show()
    {
        AppWindow.Show();
        this.CenterOnScreen();
    }

    void IDownloadProgress.Close()
    {
        Close();
    }

    void IDownloadProgress.OnDownloadProgressChanged(object sender, ItemDownloadProgressEventArgs args)
    {
        ViewModel.SetDownloadProgress((ulong)args.BytesReceived, (ulong)args.TotalBytesToReceive);
    }

    void IDownloadProgress.FinishedDownloadingFile(bool isDownloadedFileValid)
    {
        ViewModel.SetFinishedDownloading(isDownloadedFileValid);
    }

    bool IDownloadProgress.DisplayErrorMessage(string errorMessage)
    {
        // TODO: this is pretty lazy but works for now
        _ = new MessageWindow(
            "Download failed",
            errorMessage,
            "Coder Desktop Updater");
        Close();
        return true;
    }
}

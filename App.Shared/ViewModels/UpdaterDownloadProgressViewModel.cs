using System;
using Coder.Desktop.App.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using NetSparkleUpdater.Events;

namespace Coder.Desktop.App.ViewModels;

public partial class UpdaterDownloadProgressViewModel : ObservableObject
{
    // Partially implements IDownloadProgress
    public event DownloadInstallEventHandler? DownloadProcessCompleted;

    [ObservableProperty]
    public partial bool IsDownloading { get; set; } = false;

    [ObservableProperty]
    public partial string DownloadingTitle { get; set; } = "Downloading...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadProgressValue))]
    [NotifyPropertyChangedFor(nameof(UserReadableDownloadProgress))]
    public partial ulong DownloadedBytes { get; set; } = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadProgressValue))]
    [NotifyPropertyChangedFor(nameof(DownloadProgressIndeterminate))]
    [NotifyPropertyChangedFor(nameof(UserReadableDownloadProgress))]
    public partial ulong TotalBytes { get; set; } = 0; // 0 means unknown

    public int DownloadProgressValue => (int)(TotalBytes > 0 ? DownloadedBytes * 100 / TotalBytes : 0);

    public bool DownloadProgressIndeterminate => TotalBytes == 0;

    public string UserReadableDownloadProgress
    {
        get
        {
            if (DownloadProgressValue == 100)
                return "Download complete";

            // TODO: FriendlyByteConverter should allow for matching suffixes
            //       on both
            var str = FriendlyByteConverter.FriendlyBytes(DownloadedBytes) + " of ";
            if (TotalBytes > 0)
                str += FriendlyByteConverter.FriendlyBytes(TotalBytes);
            else
                str += "unknown";
            str += " downloaded";
            if (DownloadProgressValue > 0)
                str += $" ({DownloadProgressValue}%)";
            return str;
        }
    }

    // TODO: is this even necessary?
    [ObservableProperty]
    public partial string ActionButtonTitle { get; set; } = "Cancel"; // Default action string from the built-in NetSparkle UI

    [ObservableProperty]
    public partial bool IsActionButtonEnabled { get; set; } = true;

    public void SetFinishedDownloading(bool isDownloadedFileValid)
    {
        IsDownloading = false;
        TotalBytes = DownloadedBytes; // In case the total bytes were unknown
        if (isDownloadedFileValid)
        {
            DownloadingTitle = "Ready to install";
            ActionButtonTitle = "Install";
        }

        // We don't need to handle the error/invalid state here as the window
        // will handle that for us by showing a MessageWindow.
    }

    public void SetDownloadProgress(ulong bytesReceived, ulong totalBytesToReceive)
    {
        DownloadedBytes = bytesReceived;
        TotalBytes = totalBytesToReceive;
    }

    public void SetActionButtonEnabled(bool enabled)
    {
        IsActionButtonEnabled = enabled;
    }

    public void ActionButton_Click(object? sender, EventArgs e)
    {
        DownloadProcessCompleted?.Invoke(this, new DownloadInstallEventArgs(!IsDownloading));
    }
}

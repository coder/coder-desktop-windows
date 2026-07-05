using System;
using Coder.Desktop.App.Models;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Placeholder file-sync controller used by the Linux Avalonia host until
/// Mutagen plumbing is wired for this platform.
/// </summary>
public sealed class UnavailableSyncSessionController : ISyncSessionController
{
    private static readonly SyncSessionControllerStateModel CurrentState = new()
    {
        Lifecycle = SyncSessionControllerLifecycle.Stopped,
        DaemonError = "File Sync is not available in this Linux Avalonia host yet.",
        DaemonLogFilePath = "N/A",
        SyncSessions = [],
    };

    public event EventHandler<SyncSessionControllerStateModel>? StateChanged;

    public SyncSessionControllerStateModel GetState()
    {
        return CurrentState;
    }

    public Task<SyncSessionControllerStateModel> RefreshState(CancellationToken ct = default)
    {
        StateChanged?.Invoke(this, CurrentState);
        return Task.FromResult(CurrentState);
    }

    public Task<SyncSessionModel> CreateSyncSession(CreateSyncSessionRequest req, Action<string> progressCallback,
        CancellationToken ct = default)
    {
        return Task.FromException<SyncSessionModel>(CreateUnavailableException());
    }

    public Task<SyncSessionModel> PauseSyncSession(string identifier, CancellationToken ct = default)
    {
        return Task.FromException<SyncSessionModel>(CreateUnavailableException());
    }

    public Task<SyncSessionModel> ResumeSyncSession(string identifier, CancellationToken ct = default)
    {
        return Task.FromException<SyncSessionModel>(CreateUnavailableException());
    }

    public Task TerminateSyncSession(string identifier, CancellationToken ct = default)
    {
        return Task.FromException(CreateUnavailableException());
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static Exception CreateUnavailableException()
    {
        return new PlatformNotSupportedException("File Sync is not available in this Linux Avalonia host yet.");
    }
}

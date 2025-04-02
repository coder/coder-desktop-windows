using System.Collections.Generic;

namespace Coder.Desktop.App.Models;

public enum SyncSessionControllerLifecycle
{
    // Uninitialized means that the daemon has not been started yet. This can
    // be resolved by calling RefreshState (or any other RPC method
    // successfully).
    Uninitialized,

    // Stopped means that the daemon is not running. This could be because:
    // - It was never started (pre-Initialize)
    // - It was stopped due to no sync sessions (post-Initialize, post-operation)
    // - The last start attempt failed (DaemonError will be set)
    // - The last daemon process crashed (DaemonError will be set)
    Stopped,

    // Running is the normal state where the daemon is running and managing
    // sync sessions. This is only set after a successful start (including
    // being able to connect to the daemon).
    Running,
}

public class SyncSessionControllerStateModel
{
    public SyncSessionControllerLifecycle Lifecycle { get; init; } = SyncSessionControllerLifecycle.Stopped;

    /// <summary>
    ///     May be set when Lifecycle is Stopped to signify that the daemon failed
    ///     to start or unexpectedly crashed.
    /// </summary>
    public string? DaemonError { get; init; }

    public required string DaemonLogFilePath { get; init; }

    /// <summary>
    ///     This contains the last known state of all sync sessions. Sync sessions
    ///     are periodically refreshed if the daemon is running. This list is
    ///     sorted by creation time.
    /// </summary>
    public IReadOnlyList<SyncSessionModel> SyncSessions { get; init; } = [];
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.MutagenSdk;
using Coder.Desktop.MutagenSdk.Proto.Selection;
using Coder.Desktop.MutagenSdk.Proto.Service.Daemon;
using Coder.Desktop.MutagenSdk.Proto.Service.Synchronization;
using Coder.Desktop.Vpn.Utilities;
using Microsoft.Extensions.Options;
using TerminateRequest = Coder.Desktop.MutagenSdk.Proto.Service.Daemon.TerminateRequest;

namespace Coder.Desktop.App.Services;

// <summary>
// A file synchronization session to a Coder workspace agent.
// </summary>
// <remarks>
// This implementation is a placeholder while implementing the daemon lifecycle. It's implementation
// will be backed by the MutagenSDK eventually.
// </remarks>
public class SyncSession
{
    public string name { get; init; } = "";
    public string localPath { get; init; } = "";
    public string workspace { get; init; } = "";
    public string agent { get; init; } = "";
    public string remotePath { get; init; } = "";
}

public interface ISyncSessionController
{
    Task<List<SyncSession>> ListSyncSessions(CancellationToken ct);
    Task<SyncSession> CreateSyncSession(SyncSession session, CancellationToken ct);

    Task TerminateSyncSession(SyncSession session, CancellationToken ct);

    // <summary>
    // Initializes the controller; running the daemon if there are any saved sessions. Must be called and
    // complete before other methods are allowed.
    // </summary>
    Task Initialize(CancellationToken ct);
}

// These values are the config option names used in the registry. Any option
// here can be configured with `(Debug)?MutagenController:OptionName` in the registry.
//
// They should not be changed without backwards compatibility considerations.
// If changed here, they should also be changed in the installer.
public class MutagenControllerConfig
{
    [Required] public string MutagenExecutablePath { get; set; } = @"c:\mutagen.exe";
}

// <summary>
// A file synchronization controller based on the Mutagen Daemon.
// </summary>
public sealed class MutagenController : ISyncSessionController, IAsyncDisposable
{
    // Lock to protect all non-readonly class members.
    private readonly RaiiSemaphoreSlim _lock = new(1, 1);

    // daemonProcess is non-null while the daemon is running, starting, or
    // in the process of stopping.
    private Process? _daemonProcess;

    private LogWriter? _logWriter;

    // holds an in-progress task starting or stopping the daemon. If task is null,
    // then we are not starting or stopping, and the _daemonProcess will be null if
    // the daemon is currently stopped. If the task is not null, the daemon is
    // starting or stopping. If stopping, the result is null.
    private Task<MutagenClient?>? _inProgressTransition;

    // holds a client connected to the running mutagen daemon, if the daemon is running.
    private MutagenClient? _mutagenClient;

    // holds a local count of SyncSessions, primarily so we can tell when to shut down
    // the daemon because it is unneeded.
    private int _sessionCount = -1;

    // set to true if we are disposing the controller. Prevents the daemon from being
    // restarted.
    private bool _disposing;

    private readonly string _mutagenExecutablePath;


    private readonly string _mutagenDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderDesktop",
        "mutagen");

    public MutagenController(IOptions<MutagenControllerConfig> config)
    {
        _mutagenExecutablePath = config.Value.MutagenExecutablePath;
    }

    public MutagenController(string executablePath, string dataDirectory)
    {
        _mutagenExecutablePath = executablePath;
        _mutagenDataDirectory = dataDirectory;
    }

    public async ValueTask DisposeAsync()
    {
        Task<MutagenClient?>? transition = null;
        using (_ = await _lock.LockAsync(CancellationToken.None))
        {
            _disposing = true;
            if (_inProgressTransition == null && _daemonProcess == null && _mutagenClient == null) return;
            transition = _inProgressTransition;
        }

        if (transition != null) await transition;
        await StopDaemon(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        GC.SuppressFinalize(this);
    }


    public async Task<SyncSession> CreateSyncSession(SyncSession session, CancellationToken ct)
    {
        // reads of _sessionCount are atomic, so don't bother locking for this quick check.
        if (_sessionCount == -1) throw new InvalidOperationException("Controller must be Initialized first");
        var client = await EnsureDaemon(ct);
        // TODO: implement
        using (_ = await _lock.LockAsync(ct))
        {
            _sessionCount += 1;
        }

        return session;
    }


    public async Task<List<SyncSession>> ListSyncSessions(CancellationToken ct)
    {
        // reads of _sessionCount are atomic, so don't bother locking for this quick check.
        switch (_sessionCount)
        {
            case < 0:
                throw new InvalidOperationException("Controller must be Initialized first");
            case 0:
                // If we already know there are no sessions, don't start up the daemon
                // again.
                return new List<SyncSession>();
        }

        var client = await EnsureDaemon(ct);
        // TODO: implement
        return new List<SyncSession>();
    }

    public async Task Initialize(CancellationToken ct)
    {
        using (_ = await _lock.LockAsync(ct))
        {
            if (_sessionCount != -1) throw new InvalidOperationException("Initialized more than once");
            _sessionCount = -2; // in progress
        }

        var client = await EnsureDaemon(ct);
        var sessions = await client.Synchronization.ListAsync(new ListRequest
        {
            Selection = new Selection
            {
                All = true,
            },
        }, cancellationToken: ct);

        using (_ = await _lock.LockAsync(ct))
        {
            _sessionCount = sessions == null ? 0 : sessions.SessionStates.Count;
            // check first that no other transition is happening
            if (_sessionCount != 0 || _inProgressTransition != null)
                return;

            // don't pass the CancellationToken; we're not going to wait for
            // this Task anyway.
            var transition = StopDaemon(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            _inProgressTransition = transition;
            _ = transition.ContinueWith(RemoveTransition, CancellationToken.None);
            // here we don't need to wait for the transition to complete
            // before returning from Initialize(), since other operations
            // will wait for the _inProgressTransition to complete before
            // doing anything.
        }
    }

    public async Task TerminateSyncSession(SyncSession session, CancellationToken ct)
    {
        if (_sessionCount < 0) throw new InvalidOperationException("Controller must be Initialized first");
        var client = await EnsureDaemon(ct);
        // TODO: implement

        // here we don't use the Cancellation Token, since we want to decrement and possibly
        // stop the daemon even if we were cancelled, since we already successfully terminated
        // the session.
        using (_ = await _lock.LockAsync(CancellationToken.None))
        {
            _sessionCount -= 1;
            if (_sessionCount == 0)
                // check first that no other transition is happening
                if (_inProgressTransition == null)
                {
                    var transition = StopDaemon(CancellationToken.None);
                    _inProgressTransition = transition;
                    _ = transition.ContinueWith(RemoveTransition, CancellationToken.None);
                    // here we don't need to wait for the transition to complete
                    // before returning, since other operations
                    // will wait for the _inProgressTransition to complete before
                    // doing anything.
                }
        }
    }


    private async Task<MutagenClient> EnsureDaemon(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Task<MutagenClient?> transition;
            using (_ = await _lock.LockAsync(ct))
            {
                if (_disposing) throw new ObjectDisposedException(ToString(), "async disposal underway");
                if (_mutagenClient != null && _inProgressTransition == null) return _mutagenClient;
                if (_inProgressTransition != null)
                {
                    transition = _inProgressTransition;
                }
                else
                {
                    // no transition in progress, this implies the _mutagenClient
                    // must be null, and we are stopped.
                    _inProgressTransition = StartDaemon(ct);
                    transition = _inProgressTransition;
                    _ = transition.ContinueWith(RemoveTransition, ct);
                }
            }

            // wait for the transition without holding the lock.
            var result = await transition;
            if (result != null) return result;
        }
    }

    // <summary>
    // Remove the completed transition from _inProgressTransition
    // </summary>
    private void RemoveTransition(Task<MutagenClient?> transition)
    {
        using var _ = _lock.Lock();
        if (_inProgressTransition == transition) _inProgressTransition = null;
    }

    private async Task<MutagenClient?> StartDaemon(CancellationToken ct)
    {
        // stop any orphaned daemon
        try
        {
            var client = new MutagenClient(_mutagenDataDirectory);
            await client.Daemon.TerminateAsync(new TerminateRequest(), cancellationToken: ct);
        }
        catch (FileNotFoundException)
        {
            // Mainline; no daemon running.
        }

        // If we get some failure while creating the log file or starting the process, we'll retry
        // it up to 5 times x 100ms. Those issues should resolve themselves quickly if they are
        // going to at all.
        const int maxAttempts = 5;
        ListResponse? sessions = null;
        for (var attempts = 1; attempts <= maxAttempts; attempts++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (_ = await _lock.LockAsync(ct))
                {
                    StartDaemonProcessLocked();
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (attempts == maxAttempts)
                    throw;
                // back off a little and try again.
                await Task.Delay(100, ct);
                continue;
            }

            break;
        }

        return await WaitForDaemon(ct);
    }

    private async Task<MutagenClient?> WaitForDaemon(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                MutagenClient? client;
                using (_ = await _lock.LockAsync(ct))
                {
                    client = _mutagenClient ?? new MutagenClient(_mutagenDataDirectory);
                }

                _ = await client.Daemon.VersionAsync(new VersionRequest(), cancellationToken: ct);

                using (_ = await _lock.LockAsync(ct))
                {
                    if (_mutagenClient != null)
                        // Some concurrent process already wrote a client; unexpected
                        // since we should be ensuring only one transition is happening
                        // at a time. Start over with the new client.
                        continue;
                    _mutagenClient = client;
                    return _mutagenClient;
                }
            }
            catch (Exception e) when
                (e is not OperationCanceledException) // TODO: Are there other permanent errors we can detect?
            {
                // just wait a little longer for the daemon to come up
                await Task.Delay(100, ct);
            }
        }
    }

    private void StartDaemonProcessLocked()
    {
        if (_daemonProcess != null)
            throw new InvalidOperationException("startDaemonLock called when daemonProcess already present");

        // create the log file first, so ensure we have permissions
        var logPath = Path.Combine(_mutagenDataDirectory, "daemon.log");
        var logStream = new StreamWriter(logPath, true);

        _daemonProcess = new Process();
        _daemonProcess.StartInfo.FileName = _mutagenExecutablePath;
        _daemonProcess.StartInfo.Arguments = "daemon run";
        _daemonProcess.StartInfo.Environment.Add("MUTAGEN_DATA_DIRECTORY", _mutagenDataDirectory);
        // shell needs to be disabled since we set the environment
        // https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.environment?view=net-8.0
        _daemonProcess.StartInfo.UseShellExecute = false;
        _daemonProcess.StartInfo.RedirectStandardError = true;
        _daemonProcess.Start();

        var writer = new LogWriter(_daemonProcess.StandardError, logStream);
        Task.Run(() => { _ = writer.Run(); });
        _logWriter = writer;
    }

    private async Task<MutagenClient?> StopDaemon(CancellationToken ct)
    {
        Process? process;
        MutagenClient? client;
        LogWriter? writer;
        using (_ = await _lock.LockAsync(ct))
        {
            process = _daemonProcess;
            client = _mutagenClient;
            writer = _logWriter;
            _daemonProcess = null;
            _mutagenClient = null;
            _logWriter = null;
        }

        try
        {
            if (client == null)
            {
                if (process == null) return null;
                process.Kill(true);
            }
            else
            {
                try
                {
                    await client.Daemon.TerminateAsync(new TerminateRequest(), cancellationToken: ct);
                }
                catch
                {
                    if (process == null) return null;
                    process.Kill(true);
                }
            }

            if (process == null) return null;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cts.Token);
        }
        finally
        {
            client?.Dispose();
            process?.Dispose();
            writer?.Dispose();
        }

        return null;
    }
}

public class LogWriter(StreamReader reader, StreamWriter writer) : IDisposable
{
    public void Dispose()
    {
        reader.Dispose();
        writer.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task Run()
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null) await writer.WriteLineAsync(line);
        }
        catch
        {
            // TODO: Log?
        }
        finally
        {
            Dispose();
        }
    }
}

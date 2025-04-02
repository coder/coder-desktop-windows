using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.MutagenSdk;
using Coder.Desktop.MutagenSdk.Proto.Selection;
using Coder.Desktop.MutagenSdk.Proto.Service.Daemon;
using Coder.Desktop.MutagenSdk.Proto.Service.Prompting;
using Coder.Desktop.MutagenSdk.Proto.Service.Synchronization;
using Coder.Desktop.MutagenSdk.Proto.Synchronization;
using Coder.Desktop.MutagenSdk.Proto.Url;
using Coder.Desktop.Vpn.Utilities;
using Grpc.Core;
using Microsoft.Extensions.Options;
using DaemonTerminateRequest = Coder.Desktop.MutagenSdk.Proto.Service.Daemon.TerminateRequest;
using MutagenProtocol = Coder.Desktop.MutagenSdk.Proto.Url.Protocol;
using SynchronizationTerminateRequest = Coder.Desktop.MutagenSdk.Proto.Service.Synchronization.TerminateRequest;

namespace Coder.Desktop.App.Services;

public class CreateSyncSessionRequest
{
    public required Endpoint Alpha { get; init; }
    public required Endpoint Beta { get; init; }

    public class Endpoint
    {
        public enum ProtocolKind
        {
            Local,
            Ssh,
        }

        public required ProtocolKind Protocol { get; init; }
        public string User { get; init; } = "";
        public string Host { get; init; } = "";
        public uint Port { get; init; } = 0;
        public string Path { get; init; } = "";

        public URL MutagenUrl
        {
            get
            {
                var protocol = Protocol switch
                {
                    ProtocolKind.Local => MutagenProtocol.Local,
                    ProtocolKind.Ssh => MutagenProtocol.Ssh,
                    _ => throw new ArgumentException($"Invalid protocol '{Protocol}'", nameof(Protocol)),
                };

                return new URL
                {
                    Kind = Kind.Synchronization,
                    Protocol = protocol,
                    User = User,
                    Host = Host,
                    Port = Port,
                    Path = Path,
                };
            }
        }
    }
}

public interface ISyncSessionController : IAsyncDisposable
{
    public event EventHandler<SyncSessionControllerStateModel> StateChanged;

    /// <summary>
    ///     Gets the current state of the controller.
    /// </summary>
    SyncSessionControllerStateModel GetState();

    // All the following methods will raise a StateChanged event *BEFORE* they return.

    /// <summary>
    ///     Starts the daemon (if it's not running) and fully refreshes the state of the controller. This should be
    ///     called at startup and after any unexpected daemon crashes to attempt to retry.
    ///     Additionally, the first call to RefreshState will start a background task to keep the state up-to-date while
    ///     the daemon is running.
    /// </summary>
    Task<SyncSessionControllerStateModel> RefreshState(CancellationToken ct = default);

    Task<SyncSessionModel> CreateSyncSession(CreateSyncSessionRequest req, CancellationToken ct = default);
    Task<SyncSessionModel> PauseSyncSession(string identifier, CancellationToken ct = default);
    Task<SyncSessionModel> ResumeSyncSession(string identifier, CancellationToken ct = default);
    Task TerminateSyncSession(string identifier, CancellationToken ct = default);
}

// These values are the config option names used in the registry. Any option
// here can be configured with `(Debug)?AppMutagenController:OptionName` in the registry.
//
// They should not be changed without backwards compatibility considerations.
// If changed here, they should also be changed in the installer.
public class MutagenControllerConfig
{
    // This is set to "[INSTALLFOLDER]\vpn\mutagen.exe" by the installer.
    [Required] public string MutagenExecutablePath { get; set; } = @"c:\mutagen.exe";
}

/// <summary>
///     A file synchronization controller based on the Mutagen Daemon.
/// </summary>
public sealed class MutagenController : ISyncSessionController
{
    // Protects all private non-readonly class members.
    private readonly RaiiSemaphoreSlim _lock = new(1, 1);

    private readonly CancellationTokenSource _stateUpdateCts = new();
    private Task? _stateUpdateTask;

    // _state is the current state of the controller. It is updated
    // continuously while the daemon is running and after most operations.
    private SyncSessionControllerStateModel? _state;

    // _daemonProcess is non-null while the daemon is running, starting, or
    // in the process of stopping.
    private Process? _daemonProcess;

    private LogWriter? _logWriter;

    // holds a client connected to the running mutagen daemon, if the daemon is running.
    private MutagenClient? _mutagenClient;

    // set to true if we are disposing the controller. Prevents the daemon from being
    // restarted.
    private bool _disposing;

    private readonly string _mutagenExecutablePath;

    private readonly string _mutagenDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderDesktop",
        "mutagen");

    private string MutagenDaemonLog => Path.Combine(_mutagenDataDirectory, "daemon.log");

    public MutagenController(IOptions<MutagenControllerConfig> config)
    {
        _mutagenExecutablePath = config.Value.MutagenExecutablePath;
    }

    public MutagenController(string executablePath, string dataDirectory)
    {
        _mutagenExecutablePath = executablePath;
        _mutagenDataDirectory = dataDirectory;
    }

    public event EventHandler<SyncSessionControllerStateModel>? StateChanged;

    public async ValueTask DisposeAsync()
    {
        using var _ = await _lock.LockAsync(CancellationToken.None);
        _disposing = true;

        await _stateUpdateCts.CancelAsync();
        if (_stateUpdateTask != null)
            try
            {
                await _stateUpdateTask;
            }
            catch
            {
                // ignored
            }

        _stateUpdateCts.Dispose();

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StopDaemon(stopCts.Token);

        GC.SuppressFinalize(this);
    }

    public SyncSessionControllerStateModel GetState()
    {
        // No lock required to read the reference.
        var state = _state;
        // No clone needed as the model is immutable.
        if (state != null) return state;
        return new SyncSessionControllerStateModel
        {
            Lifecycle = SyncSessionControllerLifecycle.Uninitialized,
            DaemonError = null,
            DaemonLogFilePath = MutagenDaemonLog,
            SyncSessions = [],
        };
    }

    public async Task<SyncSessionControllerStateModel> RefreshState(CancellationToken ct = default)
    {
        using var _ = await _lock.LockAsync(ct);
        var client = await EnsureDaemon(ct);
        var state = await UpdateState(client, ct);
        _stateUpdateTask ??= UpdateLoop(_stateUpdateCts.Token);
        return state;
    }

    public async Task<SyncSessionModel> CreateSyncSession(CreateSyncSessionRequest req, CancellationToken ct = default)
    {
        using var _ = await _lock.LockAsync(ct);
        var client = await EnsureDaemon(ct);

        await using var prompter = await Prompter.Create(client, true, ct);
        var createRes = await client.Synchronization.CreateAsync(new CreateRequest
        {
            Prompter = prompter.Identifier,
            Specification = new CreationSpecification
            {
                Alpha = req.Alpha.MutagenUrl,
                Beta = req.Beta.MutagenUrl,
                // TODO: probably should set these at some point
                Configuration = new Configuration(),
                ConfigurationAlpha = new Configuration(),
                ConfigurationBeta = new Configuration(),
            },
        }, cancellationToken: ct);
        if (createRes == null) throw new InvalidOperationException("CreateAsync returned null");

        var session = await GetSyncSession(client, createRes.Session, ct);
        await UpdateState(client, ct);
        return session;
    }

    public async Task<SyncSessionModel> PauseSyncSession(string identifier, CancellationToken ct = default)
    {
        using var _ = await _lock.LockAsync(ct);
        var client = await EnsureDaemon(ct);

        // Pausing sessions doesn't require prompting as seen in the mutagen CLI.
        await using var prompter = await Prompter.Create(client, false, ct);
        await client.Synchronization.PauseAsync(new PauseRequest
        {
            Prompter = prompter.Identifier,
            Selection = new Selection
            {
                Specifications = { identifier },
            },
        }, cancellationToken: ct);

        var session = await GetSyncSession(client, identifier, ct);
        await UpdateState(client, ct);
        return session;
    }

    public async Task<SyncSessionModel> ResumeSyncSession(string identifier, CancellationToken ct = default)
    {
        using var _ = await _lock.LockAsync(ct);
        var client = await EnsureDaemon(ct);

        await using var prompter = await Prompter.Create(client, true, ct);
        await client.Synchronization.ResumeAsync(new ResumeRequest
        {
            Prompter = prompter.Identifier,
            Selection = new Selection
            {
                Specifications = { identifier },
            },
        }, cancellationToken: ct);

        var session = await GetSyncSession(client, identifier, ct);
        await UpdateState(client, ct);
        return session;
    }

    public async Task TerminateSyncSession(string identifier, CancellationToken ct = default)
    {
        using var _ = await _lock.LockAsync(ct);
        var client = await EnsureDaemon(ct);

        // Terminating sessions doesn't require prompting as seen in the mutagen CLI.
        await using var prompter = await Prompter.Create(client, true, ct);

        await client.Synchronization.TerminateAsync(new SynchronizationTerminateRequest
        {
            Prompter = prompter.Identifier,
            Selection = new Selection
            {
                Specifications = { identifier },
            },
        }, cancellationToken: ct);

        await UpdateState(client, ct);
    }

    private async Task UpdateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct); // 2s matches macOS app
            try
            {
                // We use a zero timeout here to avoid waiting. If another
                // operation is holding the lock, it will update the state once
                // it completes anyway.
                var locker = await _lock.LockAsync(TimeSpan.Zero, ct);
                if (locker == null) continue;
                using (locker)
                {
                    if (_mutagenClient == null) continue;
                    await UpdateState(_mutagenClient, ct);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task<SyncSessionModel> GetSyncSession(MutagenClient client, string identifier,
        CancellationToken ct)
    {
        var listRes = await client.Synchronization.ListAsync(new ListRequest
        {
            Selection = new Selection
            {
                Specifications = { identifier },
            },
        }, cancellationToken: ct);
        if (listRes == null) throw new InvalidOperationException("ListAsync returned null");
        if (listRes.SessionStates.Count != 1)
            throw new InvalidOperationException("ListAsync returned wrong number of sessions");

        return new SyncSessionModel(listRes.SessionStates[0]);
    }

    private void ReplaceState(SyncSessionControllerStateModel state)
    {
        _state = state;
        // Since the event handlers could block (or call back the
        // SyncSessionController and deadlock), we run these in a new task.
        var stateChanged = StateChanged;
        if (stateChanged == null) return;
        Task.Run(() => stateChanged.Invoke(this, state));
    }

    /// <summary>
    ///     Refreshes state and potentially stops the daemon if there are no sessions. The client must not be used after
    ///     this method is called.
    ///     Must be called AND awaited with the lock held.
    /// </summary>
    private async Task<SyncSessionControllerStateModel> UpdateState(MutagenClient client,
        CancellationToken ct = default)
    {
        ListResponse listResponse;
        try
        {
            listResponse = await client.Synchronization.ListAsync(new ListRequest
            {
                Selection = new Selection { All = true },
            }, cancellationToken: ct);
            if (listResponse == null)
                throw new InvalidOperationException("ListAsync returned null");
        }
        catch (Exception e)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var error = $"Failed to UpdateState: ListAsync: {e}";
            try
            {
                await StopDaemon(cts.Token);
            }
            catch (Exception e2)
            {
                error = $"Failed to UpdateState: StopDaemon failed after failed ListAsync call: {e2}";
            }

            ReplaceState(new SyncSessionControllerStateModel
            {
                Lifecycle = SyncSessionControllerLifecycle.Stopped,
                DaemonError = error,
                DaemonLogFilePath = MutagenDaemonLog,
                SyncSessions = [],
            });
            throw;
        }

        var lifecycle = SyncSessionControllerLifecycle.Running;
        if (listResponse.SessionStates.Count == 0)
        {
            lifecycle = SyncSessionControllerLifecycle.Stopped;
            try
            {
                await StopDaemon(ct);
            }
            catch (Exception e)
            {
                ReplaceState(new SyncSessionControllerStateModel
                {
                    Lifecycle = SyncSessionControllerLifecycle.Stopped,
                    DaemonError = $"Failed to stop daemon after no sessions: {e}",
                    DaemonLogFilePath = MutagenDaemonLog,
                    SyncSessions = [],
                });
                throw new InvalidOperationException("Failed to stop daemon after no sessions", e);
            }
        }

        var sessions = listResponse.SessionStates
            .Select(s => new SyncSessionModel(s))
            .ToList();
        sessions.Sort((a, b) => a.CreatedAt < b.CreatedAt ? -1 : 1);
        var state = new SyncSessionControllerStateModel
        {
            Lifecycle = lifecycle,
            DaemonError = null,
            DaemonLogFilePath = MutagenDaemonLog,
            SyncSessions = sessions,
        };
        ReplaceState(state);
        return state;
    }

    /// <summary>
    ///     Starts the daemon if it's not running and returns a client to it.
    ///     Must be called AND awaited with the lock held.
    /// </summary>
    private async Task<MutagenClient> EnsureDaemon(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposing, typeof(MutagenController));
        if (_mutagenClient != null && _daemonProcess != null)
            return _mutagenClient;

        try
        {
            return await StartDaemon(ct);
        }
        catch (Exception e)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await StopDaemon(cts.Token);
            }
            catch
            {
                // ignored
            }

            ReplaceState(new SyncSessionControllerStateModel
            {
                Lifecycle = SyncSessionControllerLifecycle.Stopped,
                DaemonError = $"Failed to start daemon: {e}",
                DaemonLogFilePath = MutagenDaemonLog,
                SyncSessions = [],
            });

            throw;
        }
    }

    /// <summary>
    ///     Starts the daemon and returns a client to it.
    ///     Must be called AND awaited with the lock held.
    /// </summary>
    private async Task<MutagenClient> StartDaemon(CancellationToken ct)
    {
        // Stop the running daemon
        if (_daemonProcess != null) await StopDaemon(ct);

        // Attempt to stop any orphaned daemon
        try
        {
            var client = new MutagenClient(_mutagenDataDirectory);
            await client.Daemon.TerminateAsync(new DaemonTerminateRequest(), cancellationToken: ct);
        }
        catch (FileNotFoundException)
        {
            // Mainline; no daemon running.
        }
        catch (InvalidOperationException)
        {
            // Mainline; no daemon running.
        }

        // If we get some failure while creating the log file or starting the process, we'll retry
        // it up to 5 times x 100ms. Those issues should resolve themselves quickly if they are
        // going to at all.
        const int maxAttempts = 5;
        for (var attempts = 1; attempts <= maxAttempts; attempts++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                StartDaemonProcess();
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

        // Wait for the RPC to be available.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var client = new MutagenClient(_mutagenDataDirectory);
                _ = await client.Daemon.VersionAsync(new VersionRequest(), cancellationToken: ct);
                _mutagenClient = client;
                return client;
            }
            catch (Exception e) when
                (e is not OperationCanceledException) // TODO: Are there other permanent errors we can detect?
            {
                // just wait a little longer for the daemon to come up
                await Task.Delay(100, ct);
            }
        }
    }

    /// <summary>
    ///     Starts the daemon process.
    ///     Must be called AND awaited with the lock held.
    /// </summary>
    private void StartDaemonProcess()
    {
        if (_daemonProcess != null)
            throw new InvalidOperationException("StartDaemonProcess called when _daemonProcess already present");

        // create the log file first, so ensure we have permissions
        Directory.CreateDirectory(_mutagenDataDirectory);
        var logPath = Path.Combine(_mutagenDataDirectory, "daemon.log");
        var logStream = new StreamWriter(logPath, true);

        _daemonProcess = new Process();
        _daemonProcess.StartInfo.FileName = _mutagenExecutablePath;
        _daemonProcess.StartInfo.Arguments = "daemon run";
        _daemonProcess.StartInfo.Environment.Add("MUTAGEN_DATA_DIRECTORY", _mutagenDataDirectory);
        // hide the console window
        _daemonProcess.StartInfo.CreateNoWindow = true;
        // shell needs to be disabled since we set the environment
        // https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.environment?view=net-8.0
        _daemonProcess.StartInfo.UseShellExecute = false;
        _daemonProcess.StartInfo.RedirectStandardError = true;
        // TODO: log exited process
        // _daemonProcess.Exited += ...
        if (!_daemonProcess.Start())
            throw new InvalidOperationException("Failed to start mutagen daemon process, Start returned false");

        var writer = new LogWriter(_daemonProcess.StandardError, logStream);
        Task.Run(() => { _ = writer.Run(); });
        _logWriter = writer;
    }

    /// <summary>
    ///     Stops the daemon process.
    ///     Must be called AND awaited with the lock held.
    /// </summary>
    private async Task StopDaemon(CancellationToken ct)
    {
        var process = _daemonProcess;
        var client = _mutagenClient;
        var writer = _logWriter;
        _daemonProcess = null;
        _mutagenClient = null;
        _logWriter = null;

        try
        {
            if (client == null)
            {
                if (process == null) return;
                process.Kill(true);
            }
            else
            {
                try
                {
                    await client.Daemon.TerminateAsync(new DaemonTerminateRequest(), cancellationToken: ct);
                }
                catch
                {
                    if (process == null) return;
                    process.Kill(true);
                }
            }

            if (process == null) return;
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
    }

    private class Prompter : IAsyncDisposable
    {
        private readonly AsyncDuplexStreamingCall<HostRequest, HostResponse> _dup;
        private readonly CancellationTokenSource _cts;
        private readonly Task _handleRequestsTask;
        public string Identifier { get; }

        private Prompter(string identifier, AsyncDuplexStreamingCall<HostRequest, HostResponse> dup,
            CancellationToken ct)
        {
            Identifier = identifier;
            _dup = dup;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _handleRequestsTask = HandleRequests(_cts.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await _handleRequestsTask;
            }
            catch
            {
                // ignored
            }

            _cts.Dispose();
            GC.SuppressFinalize(this);
        }

        public static async Task<Prompter> Create(MutagenClient client, bool allowPrompts = false,
            CancellationToken ct = default)
        {
            var dup = client.Prompting.Host(cancellationToken: ct);
            if (dup == null) throw new InvalidOperationException("Prompting.Host returned null");

            try
            {
                // Write first request.
                await dup.RequestStream.WriteAsync(new HostRequest
                {
                    AllowPrompts = allowPrompts,
                }, ct);

                // Read initial response.
                if (!await dup.ResponseStream.MoveNext(ct))
                    throw new InvalidOperationException("Prompting.Host response stream ended early");
                var response = dup.ResponseStream.Current;
                if (response == null)
                    throw new InvalidOperationException("Prompting.Host response stream returned null");
                if (string.IsNullOrEmpty(response.Identifier))
                    throw new InvalidOperationException("Prompting.Host response stream returned empty identifier");

                return new Prompter(response.Identifier, dup, ct);
            }
            catch
            {
                await dup.RequestStream.CompleteAsync();
                dup.Dispose();
                throw;
            }
        }

        private async Task HandleRequests(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Read next request and validate it.
                    if (!await _dup.ResponseStream.MoveNext(ct))
                        throw new InvalidOperationException("Prompting.Host response stream ended early");
                    var response = _dup.ResponseStream.Current;
                    if (response == null)
                        throw new InvalidOperationException("Prompting.Host response stream returned null");
                    if (response.Message == null)
                        throw new InvalidOperationException("Prompting.Host response stream returned a null message");

                    // Currently we only reply to SSH fingerprint messages with
                    // "yes" and send an empty reply for everything else.
                    var reply = "";
                    if (response.IsPrompt && response.Message.Contains("yes/no/[fingerprint]")) reply = "yes";

                    await _dup.RequestStream.WriteAsync(new HostRequest
                    {
                        Response = reply,
                    }, ct);
                }
            }
            catch
            {
                await _dup.RequestStream.CompleteAsync();
                // TODO: log?
            }
        }
    }

    private class LogWriter(StreamReader reader, StreamWriter writer) : IDisposable
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
                while (await reader.ReadLineAsync() is { } line) await writer.WriteLineAsync(line);
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
}

using System;
using System.Collections.Generic;
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
    Task<IEnumerable<SyncSessionModel>> ListSyncSessions(CancellationToken ct = default);
    Task<SyncSessionModel> CreateSyncSession(CreateSyncSessionRequest req, CancellationToken ct = default);
    Task<SyncSessionModel> PauseSyncSession(string identifier, CancellationToken ct = default);
    Task<SyncSessionModel> ResumeSyncSession(string identifier, CancellationToken ct = default);
    Task TerminateSyncSession(string identifier, CancellationToken ct = default);

    // <summary>
    // Initializes the controller; running the daemon if there are any saved sessions. Must be called and
    // complete before other methods are allowed.
    // </summary>
    Task Initialize(CancellationToken ct = default);
}

// These values are the config option names used in the registry. Any option
// here can be configured with `(Debug)?AppMutagenController:OptionName` in the registry.
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
        Task<MutagenClient?>? transition;
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

    public async Task<SyncSessionModel> CreateSyncSession(CreateSyncSessionRequest req, CancellationToken ct = default)
    {
        // reads of _sessionCount are atomic, so don't bother locking for this quick check.
        if (_sessionCount == -1) throw new InvalidOperationException("Controller must be Initialized first");
        var client = await EnsureDaemon(ct);

        await using var prompter = await CreatePrompter(client, true, ct);
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

        // Increment session count early, to avoid list failures interfering
        // with the count.
        using (_ = await _lock.LockAsync(ct))
        {
            _sessionCount += 1;
        }

        var listRes = await client.Synchronization.ListAsync(new ListRequest
        {
            Selection = new Selection
            {
                Specifications = { createRes.Session },
            },
        }, cancellationToken: ct);
        if (listRes == null) throw new InvalidOperationException("ListAsync returned null");
        if (listRes.SessionStates.Count != 1)
            throw new InvalidOperationException("ListAsync returned wrong number of sessions");

        return new SyncSessionModel(listRes.SessionStates[0]);
    }

    public async Task<SyncSessionModel> PauseSyncSession(string identifier, CancellationToken ct = default)
    {
        // reads of _sessionCount are atomic, so don't bother locking for this quick check.
        if (_sessionCount == -1) throw new InvalidOperationException("Controller must be Initialized first");
        var client = await EnsureDaemon(ct);

        // Pausing sessions doesn't require prompting as seen in the mutagen CLI.
        await using var prompter = await CreatePrompter(client, false, ct);
        _ = await client.Synchronization.PauseAsync(new PauseRequest
        {
            Prompter = prompter.Identifier,
            Selection = new Selection
            {
                Specifications = { identifier },
            },
        }, cancellationToken: ct);

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

    public async Task<SyncSessionModel> ResumeSyncSession(string identifier, CancellationToken ct = default)
    {
        // reads of _sessionCount are atomic, so don't bother locking for this quick check.
        if (_sessionCount == -1) throw new InvalidOperationException("Controller must be Initialized first");
        var client = await EnsureDaemon(ct);

        // Resuming sessions doesn't require prompting as seen in the mutagen CLI.
        await using var prompter = await CreatePrompter(client, false, ct);
        _ = await client.Synchronization.ResumeAsync(new ResumeRequest
        {
            Prompter = prompter.Identifier,
            Selection = new Selection
            {
                Specifications = { identifier },
            },
        }, cancellationToken: ct);

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

    public async Task<IEnumerable<SyncSessionModel>> ListSyncSessions(CancellationToken ct = default)
    {
        // reads of _sessionCount are atomic, so don't bother locking for this quick check.
        switch (_sessionCount)
        {
            case < 0:
                throw new InvalidOperationException("Controller must be Initialized first");
            case 0:
                // If we already know there are no sessions, don't start up the daemon
                // again.
                return [];
        }

        var client = await EnsureDaemon(ct);
        var res = await client.Synchronization.ListAsync(new ListRequest
        {
            Selection = new Selection { All = true },
        }, cancellationToken: ct);

        if (res == null) return [];
        return res.SessionStates.Select(s => new SyncSessionModel(s));

        // TODO: the daemon should be stopped if there are no sessions.
    }

    public async Task Initialize(CancellationToken ct = default)
    {
        using (_ = await _lock.LockAsync(ct))
        {
            if (_sessionCount != -1) throw new InvalidOperationException("Initialized more than once");
            _sessionCount = -2; // in progress
        }

        var client = await EnsureDaemon(ct);
        var sessions = await client.Synchronization.ListAsync(new ListRequest
        {
            Selection = new Selection { All = true },
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

    public async Task TerminateSyncSession(string identifier, CancellationToken ct = default)
    {
        if (_sessionCount < 0) throw new InvalidOperationException("Controller must be Initialized first");
        var client = await EnsureDaemon(ct);

        // Terminating sessions doesn't require prompting as seen in the mutagen CLI.
        await using var prompter = await CreatePrompter(client, true, ct);

        _ = await client.Synchronization.TerminateAsync(new SynchronizationTerminateRequest
        {
            Prompter = prompter.Identifier,
            Selection = new Selection
            {
                Specifications = { identifier },
            },
        }, cancellationToken: ct);

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
                    await client.Daemon.TerminateAsync(new DaemonTerminateRequest(), cancellationToken: ct);
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

    private static async Task<Prompter> CreatePrompter(MutagenClient client, bool allowPrompts = false,
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

    private class Prompter : IAsyncDisposable
    {
        private readonly AsyncDuplexStreamingCall<HostRequest, HostResponse> _dup;
        private readonly CancellationTokenSource _cts;
        private readonly Task _handleRequestsTask;
        public string Identifier { get; }

        public Prompter(string identifier, AsyncDuplexStreamingCall<HostRequest, HostResponse> dup,
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

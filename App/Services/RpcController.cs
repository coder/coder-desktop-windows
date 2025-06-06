using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.Vpn;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Utilities;

namespace Coder.Desktop.App.Services;

public class RpcOperationException : Exception
{
    public RpcOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public RpcOperationException(string message) : base(message)
    {
    }
}

public class VpnLifecycleException : Exception
{
    public VpnLifecycleException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public VpnLifecycleException(string message) : base(message)
    {
    }
}

public interface IRpcController : IAsyncDisposable
{
    public event EventHandler<RpcModel> StateChanged;

    /// <summary>
    ///     Get the current state of the RpcController and the latest state received from the service.
    /// </summary>
    public RpcModel GetState();

    /// <summary>
    ///     Disconnect from and reconnect to the RPC server.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another operation is in progress</exception>
    /// <exception>Throws an exception if reconnection fails. Exceptions from disconnection are ignored.</exception>
    public Task Reconnect(CancellationToken ct = default);

    /// <summary>
    ///     Start the VPN using the stored credentials in the ICredentialManager. If the VPN is already running, this
    ///     may have no effect.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another operation is in progress</exception>
    /// <exception cref="RpcOperationException">If the sending of the start command fails</exception>
    /// <exception cref="VpnLifecycleException">If the service reports that the VPN failed to start</exception>
    public Task StartVpn(CancellationToken ct = default);

    /// <summary>
    ///     Stop the VPN. If the VPN is already not running, this may have no effect.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another operation is in progress</exception>
    /// <exception cref="RpcOperationException">If the sending of the stop command fails</exception>
    /// <exception cref="VpnLifecycleException">If the service reports that the VPN failed to stop</exception>
    public Task StopVpn(CancellationToken ct = default);
}

public class RpcController : IRpcController
{
    private readonly ICredentialManager _credentialManager;

    private readonly RaiiSemaphoreSlim _operationLock = new(1, 1);
    private Speaker<ClientMessage, ServiceMessage>? _speaker;

    private readonly RaiiSemaphoreSlim _stateLock = new(1, 1);
    private readonly RpcModel _state = new();

    public RpcController(ICredentialManager credentialManager)
    {
        _credentialManager = credentialManager;
    }

    public event EventHandler<RpcModel>? StateChanged;

    public RpcModel GetState()
    {
        using var _ = _stateLock.Lock();
        return _state.Clone();
    }

    public async Task Reconnect(CancellationToken ct = default)
    {
        using var _ = await AcquireOperationLockNowAsync();
        MutateState(state =>
        {
            state.RpcLifecycle = RpcLifecycle.Connecting;
            state.VpnLifecycle = VpnLifecycle.Stopped;
            state.Workspaces = [];
            state.Agents = [];
        });

        if (_speaker != null)
            try
            {
                await DisposeSpeaker();
            }
            catch (Exception e)
            {
                // TODO: log/notify?
                Debug.WriteLine($"Error disposing existing Speaker: {e}");
            }

        try
        {
            var client =
                new NamedPipeClientStream(".", "Coder.Desktop.Vpn", PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(ct);
            _speaker = new Speaker<ClientMessage, ServiceMessage>(client);
            _speaker.Receive += SpeakerOnReceive;
            _speaker.Error += SpeakerOnError;
            await _speaker.StartAsync(ct);
        }
        catch (Exception e)
        {
            MutateState(state =>
            {
                state.RpcLifecycle = RpcLifecycle.Disconnected;
                state.VpnLifecycle = VpnLifecycle.Unknown;
                state.Workspaces = [];
                state.Agents = [];
            });
            throw new RpcOperationException("Failed to reconnect to the RPC server", e);
        }

        MutateState(state =>
        {
            state.RpcLifecycle = RpcLifecycle.Connected;
            state.VpnLifecycle = VpnLifecycle.Unknown;
            state.Workspaces = [];
            state.Agents = [];
        });

        var statusReply = await _speaker.SendRequestAwaitReply(new ClientMessage
        {
            Status = new StatusRequest(),
        }, ct);
        if (statusReply.MsgCase != ServiceMessage.MsgOneofCase.Status)
            throw new VpnLifecycleException(
                $"Failed to get VPN status. Unexpected reply message type: {statusReply.MsgCase}");
        ApplyStatusUpdate(statusReply.Status);
    }

    public async Task StartVpn(CancellationToken ct = default)
    {
        using var _ = await AcquireOperationLockNowAsync();
        AssertRpcConnected();

        var credentials = _credentialManager.GetCachedCredentials();
        if (credentials.State != CredentialState.Valid)
            throw new RpcOperationException(
                $"Cannot start VPN without valid credentials, current state: {credentials.State}");

        MutateState(state =>
        {
            state.VpnLifecycle = VpnLifecycle.Starting;
            state.VpnStartupProgress = new VpnStartupProgress();
        });

        ServiceMessage reply;
        try
        {
            reply = await _speaker!.SendRequestAwaitReply(new ClientMessage
            {
                Start = new StartRequest
                {
                    CoderUrl = credentials.CoderUrl?.ToString(),
                    ApiToken = credentials.ApiToken,
                },
            }, ct);
        }
        catch (Exception e)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
            throw new RpcOperationException("Failed to send start command to service", e);
        }

        if (reply.MsgCase != ServiceMessage.MsgOneofCase.Start)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
            throw new VpnLifecycleException($"Failed to start VPN. Unexpected reply message type: {reply.MsgCase}");
        }

        if (!reply.Start.Success)
        {
            // We use Stopped instead of Unknown here as it's usually the case
            // that a failed start got cleaned up successfully.
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
            throw new VpnLifecycleException(
                $"Failed to start VPN. Service reported failure: {reply.Start.ErrorMessage}");
        }

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Started; });
    }

    public async Task StopVpn(CancellationToken ct = default)
    {
        using var _ = await AcquireOperationLockNowAsync();
        AssertRpcConnected();

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopping; });

        ServiceMessage reply;
        try
        {
            reply = await _speaker!.SendRequestAwaitReply(new ClientMessage
            {
                Stop = new StopRequest(),
            }, ct);
        }
        catch (Exception e)
        {
            throw new RpcOperationException("Failed to send stop command to service", e);
        }
        finally
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
        }

        if (reply.MsgCase != ServiceMessage.MsgOneofCase.Stop)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
            throw new VpnLifecycleException($"Failed to stop VPN. Unexpected reply message type: {reply.MsgCase}");
        }

        if (!reply.Stop.Success)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
            throw new VpnLifecycleException($"Failed to stop VPN. Service reported failure: {reply.Stop.ErrorMessage}");
        }

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
    }

    public async ValueTask DisposeAsync()
    {
        if (_speaker != null)
            await _speaker.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void MutateState(Action<RpcModel> mutator)
    {
        RpcModel newState;
        using (_stateLock.Lock())
        {
            mutator(_state);
            // Unset the startup progress if the VpnLifecycle is not Starting
            if (_state.VpnLifecycle != VpnLifecycle.Starting)
                _state.VpnStartupProgress = null;
            newState = _state.Clone();
        }

        StateChanged?.Invoke(this, newState);
    }

    private async Task<IDisposable> AcquireOperationLockNowAsync()
    {
        var locker = await _operationLock.LockAsync(TimeSpan.Zero);
        if (locker == null)
            throw new InvalidOperationException("Cannot perform operation while another operation is in progress");
        return locker;
    }

    private void ApplyStatusUpdate(Status status)
    {
        MutateState(state =>
        {
            state.VpnLifecycle = status.Lifecycle switch
            {
                Status.Types.Lifecycle.Unknown => VpnLifecycle.Unknown,
                Status.Types.Lifecycle.Starting => VpnLifecycle.Starting,
                Status.Types.Lifecycle.Started => VpnLifecycle.Started,
                Status.Types.Lifecycle.Stopping => VpnLifecycle.Stopping,
                Status.Types.Lifecycle.Stopped => VpnLifecycle.Stopped,
                _ => VpnLifecycle.Stopped,
            };
            state.Workspaces = status.PeerUpdate.UpsertedWorkspaces;
            state.Agents = status.PeerUpdate.UpsertedAgents;
        });
    }

    private void ApplyStartProgressUpdate(StartProgress message)
    {
        MutateState(state =>
        {
            // MutateState will undo these changes if it doesn't believe we're
            // in the "Starting" state.
            state.VpnStartupProgress = VpnStartupProgress.FromProto(message);
        });
    }

    private void SpeakerOnReceive(ReplyableRpcMessage<ClientMessage, ServiceMessage> message)
    {
        switch (message.Message.MsgCase)
        {
            case ServiceMessage.MsgOneofCase.Start:
            case ServiceMessage.MsgOneofCase.Stop:
            case ServiceMessage.MsgOneofCase.Status:
                ApplyStatusUpdate(message.Message.Status);
                break;
            case ServiceMessage.MsgOneofCase.StartProgress:
                ApplyStartProgressUpdate(message.Message.StartProgress);
                break;
            case ServiceMessage.MsgOneofCase.None:
            default:
                // TODO: log unexpected message
                break;
        }
    }

    private async Task DisposeSpeaker()
    {
        if (_speaker == null) return;
        _speaker.Receive -= SpeakerOnReceive;
        _speaker.Error -= SpeakerOnError;
        await _speaker.DisposeAsync();
        _speaker = null;
    }

    private void SpeakerOnError(Exception e)
    {
        Debug.WriteLine($"Error: {e}");
        try
        {
            Reconnect(CancellationToken.None).Wait();
        }
        catch
        {
            // best effort to immediately reconnect
        }
    }

    private void AssertRpcConnected()
    {
        if (_speaker == null)
            throw new InvalidOperationException("Not connected to the RPC server");
    }
}

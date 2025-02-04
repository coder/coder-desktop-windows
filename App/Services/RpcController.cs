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

public interface IRpcController
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
            state.VisibleAgents.Clear();
        });

        if (_speaker != null)
            try
            {
                await _speaker.DisposeAsync();
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
                state.VpnLifecycle = VpnLifecycle.Stopped;
                state.VisibleAgents.Clear();
            });
            throw new RpcOperationException("Failed to reconnect to the RPC server", e);
        }

        MutateState(state =>
        {
            state.RpcLifecycle = RpcLifecycle.Connected;
            // TODO: fetch current state
            state.VpnLifecycle = VpnLifecycle.Stopped;
            state.VisibleAgents.Clear();
        });
    }

    public async Task StartVpn(CancellationToken ct = default)
    {
        using var _ = await AcquireOperationLockNowAsync();
        EnsureRpcConnected();

        var credentials = _credentialManager.GetCredentials();
        if (credentials.State != CredentialState.Valid)
            throw new RpcOperationException("Cannot start VPN without valid credentials");

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Starting; });

        ServiceMessage reply;
        try
        {
            reply = await _speaker!.SendRequestAwaitReply(new ClientMessage
            {
                Start = new StartRequest
                {
                    CoderUrl = credentials.CoderUrl,
                    ApiToken = credentials.ApiToken,
                },
            }, ct);
            if (reply.MsgCase != ServiceMessage.MsgOneofCase.Start)
                throw new InvalidOperationException($"Unexpected reply message type: {reply.MsgCase}");
        }
        catch (Exception e)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
            throw new RpcOperationException("Failed to send start command to service", e);
        }

        if (!reply.Start.Success)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
            throw new VpnLifecycleException("Failed to start VPN",
                new InvalidOperationException($"Service reported failure: {reply.Start.ErrorMessage}"));
        }

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Started; });
    }

    public async Task StopVpn(CancellationToken ct = default)
    {
        using var _ = await AcquireOperationLockNowAsync();
        EnsureRpcConnected();

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
            // Technically the state is unknown now.
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
        }

        if (reply.MsgCase != ServiceMessage.MsgOneofCase.Stop)
            throw new VpnLifecycleException("Failed to stop VPN",
                new InvalidOperationException($"Unexpected reply message type: {reply.MsgCase}"));
        if (!reply.Stop.Success)
            throw new VpnLifecycleException("Failed to stop VPN",
                new InvalidOperationException($"Service reported failure: {reply.Stop.ErrorMessage}"));
    }

    private void MutateState(Action<RpcModel> mutator)
    {
        using var _ = _stateLock.Lock();
        mutator(_state);
        StateChanged?.Invoke(this, _state.Clone());
    }

    private async Task<IDisposable> AcquireOperationLockNowAsync()
    {
        var locker = await _operationLock.LockAsync(TimeSpan.Zero);
        if (locker == null)
            throw new InvalidOperationException("Cannot perform operation while another operation is in progress");
        return locker;
    }

    private void SpeakerOnReceive(ReplyableRpcMessage<ClientMessage, ServiceMessage> message)
    {
        // TODO: this
    }

    private void SpeakerOnError(Exception e)
    {
        Debug.WriteLine($"Error: {e}");
        Reconnect(CancellationToken.None).Wait();
    }

    private void EnsureRpcConnected()
    {
        if (_speaker == null)
            throw new InvalidOperationException("Not connected to the RPC server");
    }
}

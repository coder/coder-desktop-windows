using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Coder.Desktop.Vpn.Proto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coder.Desktop.Vpn.Service;

public class ManagerRpcClient(Speaker<ServiceMessage, ClientMessage> speaker, Task task)
{
    public Speaker<ServiceMessage, ClientMessage> Speaker { get; } = speaker;
    public Task Task { get; } = task;
}

/// <summary>
///     Provides a named pipe server for communication between multiple RpcRole.Client and RpcRole.Manager.
/// </summary>
public class ManagerRpcService : BackgroundService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<ulong, ManagerRpcClient> _activeClients = new();
    private readonly ManagerConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<ManagerRpcService> _logger;
    private readonly IManager _manager;
    private ulong _lastClientId;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ManagerRpcService(IOptions<ManagerConfig> config, ILogger<ManagerRpcService> logger, IManager manager)
    {
        _logger = logger;
        _manager = manager;
        _config = config.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        while (!_activeClients.IsEmpty) await Task.WhenAny(_activeClients.Values.Select(c => c.Task));
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        while (!_activeClients.IsEmpty) await Task.WhenAny(_activeClients.Values.Select(c => c.Task));
    }

    /// <summary>
    ///     Starts the named pipe server, listens for incoming connections and starts handling them asynchronously.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(@"Starting continuous named pipe RPC server at \\.\pipe\{PipeName}",
            _config.ServiceRpcPipeName);

        // Allow everyone to connect to the named pipe
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Starting a named pipe server is not like a TCP server where you can
        // continuously accept new connections. You need to recreate the server
        // after accepting a connection in order to accept new connections.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);
        while (!linkedCts.IsCancellationRequested)
        {
            var pipeServer = NamedPipeServerStreamAcl.Create(_config.ServiceRpcPipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0,
                0, pipeSecurity);

            try
            {
                _logger.LogDebug("Waiting for new named pipe client connection");
                await pipeServer.WaitForConnectionAsync(linkedCts.Token);

                var clientId = Interlocked.Add(ref _lastClientId, 1);
                _logger.LogInformation("Handling named pipe client connection for client {ClientId}", clientId);
                var speaker = new Speaker<ServiceMessage, ClientMessage>(pipeServer);
                var clientTask = HandleRpcClientAsync(speaker, linkedCts.Token);
                _activeClients.TryAdd(clientId, new ManagerRpcClient(speaker, clientTask));
                _ = clientTask.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                        _logger.LogWarning(task.Exception, "Client {ClientId} RPC task faulted", clientId);
                    _activeClients.TryRemove(clientId, out _);
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await pipeServer.DisposeAsync();
                throw;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to accept named pipe client");
                await pipeServer.DisposeAsync();
            }
        }
    }

    private async Task HandleRpcClientAsync(Speaker<ServiceMessage, ClientMessage> speaker, CancellationToken ct)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await using (speaker)
        {
            var tcs = new TaskCompletionSource();
            var activeTasks = new ConcurrentDictionary<int, Task>();
            speaker.Receive += msg =>
            {
                var task = HandleRpcMessageAsync(msg, linkedCts.Token);
                activeTasks.TryAdd(task.Id, task);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger.LogWarning(t.Exception, "Client RPC message handler task faulted");
                    activeTasks.TryRemove(t.Id, out _);
                }, CancellationToken.None);
            };
            speaker.Error += tcs.SetException;
            speaker.Error += exception => { _logger.LogWarning(exception, "Client RPC speaker error"); };
            await using (ct.Register(() => tcs.SetCanceled(ct)))
            {
                await speaker.StartAsync(ct);
                await tcs.Task;
                await linkedCts.CancelAsync();
                while (!activeTasks.IsEmpty)
                    await Task.WhenAny(activeTasks.Values);
            }
        }
    }

    private async Task HandleRpcMessageAsync(ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct)
    {
        _logger.LogInformation("Received RPC message: {Message}", message.Message);
        await _manager.HandleClientRpcMessage(message, ct);
    }

    public async Task BroadcastAsync(ServiceMessage message, CancellationToken ct)
    {
        // Looping over a ConcurrentDictionary is exception-safe, but any items
        // added or removed during the loop may or may not be included.
        foreach (var (clientId, client) in _activeClients)
            try
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(5 * 1000);
                await client.Speaker.SendMessage(message, cts.Token);
            }
            catch (ObjectDisposedException)
            {
                // The speaker was likely closed while we were iterating.
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to send message to client {ClientId}", clientId);
                // TODO: this should probably kill the client, but due to the
                //       async nature of the client handling, calling Dispose
                //       will not remove the client from the active clients list
            }
    }
}

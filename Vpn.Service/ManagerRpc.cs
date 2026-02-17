using System.Collections.Concurrent;
using Coder.Desktop.Vpn;
using Coder.Desktop.Vpn.Proto;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.Vpn.Service;

public class ManagerRpcClient(Speaker<ServiceMessage, ClientMessage> speaker, Task task)
{
    public Speaker<ServiceMessage, ClientMessage> Speaker { get; } = speaker;
    public Task Task { get; } = task;
}

public interface IManagerRpc : IAsyncDisposable
{
    delegate Task OnReceiveHandler(ulong clientId, ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct = default);

    event OnReceiveHandler? OnReceive;

    Task StopAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(CancellationToken stoppingToken);
    Task BroadcastAsync(ServiceMessage message, CancellationToken ct = default);
}

/// <summary>
///     Provides an RPC server for communication between multiple clients and the Manager.
///     Uses IRpcServerTransport for platform-specific transport (Named Pipes on Windows, Unix Sockets on Linux).
/// </summary>
public class ManagerRpc : IManagerRpc
{
    private readonly ConcurrentDictionary<ulong, ManagerRpcClient> _activeClients = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<ManagerRpc> _logger;
    private readonly IRpcServerTransport _transport;
    private ulong _lastClientId;

    public ManagerRpc(IRpcServerTransport transport, ILogger<ManagerRpc> logger)
    {
        _logger = logger;
        _transport = transport;
    }

    public event IManagerRpc.OnReceiveHandler? OnReceive;

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            while (!_activeClients.IsEmpty)
                await Task.WhenAny(_activeClients.Values.Select(c => c.Task));
        }
        catch
        {
        }

        await _transport.DisposeAsync();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        while (!_activeClients.IsEmpty) await Task.WhenAny(_activeClients.Values.Select(c => c.Task));
    }

    /// <summary>
    ///     Starts the RPC server, listens for incoming connections and starts handling them asynchronously.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RPC server");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);
        while (!linkedCts.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                _logger.LogDebug("Waiting for new client connection");
                stream = await _transport.AcceptAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to accept client connection");
                continue;
            }

            var clientId = Interlocked.Add(ref _lastClientId, 1);
            _logger.LogInformation("Handling client connection for client {ClientId}", clientId);
            var speaker = new Speaker<ServiceMessage, ClientMessage>(stream);
            var clientTask = HandleRpcClientAsync(clientId, speaker, linkedCts.Token);
            _activeClients.TryAdd(clientId, new ManagerRpcClient(speaker, clientTask));
            _ = clientTask.ContinueWith(task =>
            {
                if (task.IsFaulted)
                    _logger.LogWarning(task.Exception, "Client {ClientId} RPC task faulted", clientId);
                _activeClients.TryRemove(clientId, out _);
            }, CancellationToken.None);
        }
    }

    public async Task BroadcastAsync(ServiceMessage message, CancellationToken ct)
    {
        await Task.WhenAll(_activeClients.Select(async item =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await item.Value.Speaker.SendMessage(message, cts.Token);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to send message to client {ClientId}", item.Key);
            }
        }));
    }

    private async Task HandleRpcClientAsync(ulong clientId, Speaker<ServiceMessage, ClientMessage> speaker,
        CancellationToken ct)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await using (speaker)
        {
            var tcs = new TaskCompletionSource();
            var activeTasks = new ConcurrentDictionary<int, Task>();
            speaker.Receive += msg =>
            {
                var task = HandleRpcMessageAsync(clientId, msg, linkedCts.Token);
                activeTasks.TryAdd(task.Id, task);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger.LogWarning(t.Exception, "Client {ClientId} RPC message handler task faulted", clientId);
                    activeTasks.TryRemove(t.Id, out _);
                }, CancellationToken.None);
            };
            speaker.Error += tcs.SetException;
            speaker.Error += exception =>
            {
                _logger.LogWarning(exception, "Client {clientId} RPC speaker error", clientId);
            };
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

    private async Task HandleRpcMessageAsync(ulong clientId, ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct)
    {
        _logger.LogInformation("Received RPC message from client {ClientId}: {Message}", clientId, message.Message);
        foreach (var handler in OnReceive?.GetInvocationList().Cast<IManagerRpc.OnReceiveHandler>() ?? [])
            try
            {
                await handler(clientId, message, ct);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to handle RPC message from client {ClientId} with handler", clientId);
            }
    }
}

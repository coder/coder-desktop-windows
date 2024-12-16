using System.Collections.Concurrent;
using System.IO.Pipes;
using Coder.Desktop.Vpn.Proto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coder.Desktop.Vpn.Service;

/// <summary>
///     Provides a named pipe server for communication between multiple RpcRole.Client and RpcRole.Manager.
/// </summary>
public class ManagerRpcService : BackgroundService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, Task> _activeClientTasks = new();
    private readonly ManagerConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<ManagerRpcService> _logger;
    private readonly IManager _manager;

    public ManagerRpcService(IOptions<ManagerConfig> config, ILogger<ManagerRpcService> logger, IManager manager)
    {
        _logger = logger;
        _manager = manager;
        _config = config.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        while (!_activeClientTasks.IsEmpty) await Task.WhenAny(_activeClientTasks.Values);
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        while (!_activeClientTasks.IsEmpty) await Task.WhenAny(_activeClientTasks.Values);
    }

    /// <summary>
    ///     Starts the named pipe server, listens for incoming connections and starts handling them asynchronously.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(@"Starting continuous named pipe RPC server at \\.\pipe\{PipeName}",
            _config.ServiceRpcPipeName);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);
        while (!linkedCts.IsCancellationRequested)
        {
            var pipeServer = new NamedPipeServerStream(_config.ServiceRpcPipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            try
            {
                try
                {
                    _logger.LogDebug("Waiting for new named pipe client connection");
                    await pipeServer.WaitForConnectionAsync(linkedCts.Token);
                }
                finally
                {
                    await pipeServer.DisposeAsync();
                }

                _logger.LogInformation("Handling named pipe client connection");
                var clientTask = HandleRpcClientAsync(pipeServer, linkedCts.Token);
                _activeClientTasks.TryAdd(clientTask.Id, clientTask);
                _ = clientTask.ContinueWith(RpcClientContinuation, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to accept named pipe client");
            }
        }
    }

    private async Task HandleRpcClientAsync(NamedPipeServerStream pipeServer, CancellationToken ct)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await using (pipeServer)
        {
            await using var speaker = new Speaker<ServiceMessage, ClientMessage>(pipeServer);

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

    private void RpcClientContinuation(Task task)
    {
        if (task.IsFaulted)
            _logger.LogWarning(task.Exception, "Client RPC task faulted");
        _activeClientTasks.TryRemove(task.Id, out _);
    }

    private async Task HandleRpcMessageAsync(ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct)
    {
        _logger.LogInformation("Received RPC message: {Message}", message.Message);
        await _manager.HandleClientRpcMessage(message, ct);
    }
}

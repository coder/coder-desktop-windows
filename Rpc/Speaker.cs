using System.Collections.Concurrent;
using System.Text;
using Coder.Desktop.Rpc.Proto;
using Coder.Desktop.Rpc.Utilities;
using Google.Protobuf;

namespace Coder.Desktop.Rpc;

/// <summary>
///     Wraps a <c>RpcMessage</c> to allow easily sending a reply via the <c>Speaker</c>.
/// </summary>
/// <param name="speaker">Speaker to use for sending reply</param>
/// <param name="message">Original received message</param>
public class ReplyableRpcMessage<TS, TR>(Speaker<TS, TR> speaker, TR message) : RpcMessage<TR>
    where TS : RpcMessage<TS>, IMessage<TS>
    where TR : RpcMessage<TR>, IMessage<TR>, new()
{
    public override RPC RpcField
    {
        get => message.RpcField;
        set => message.RpcField = value;
    }

    public override TR Message => message;

    /// <summary>
    ///     Sends a reply to the original message.
    /// </summary>
    /// <param name="reply">Correct reply message</param>
    /// <param name="ct">Optional cancellation token</param>
    public async Task SendReply(TS reply, CancellationToken ct = default)
    {
        await speaker.SendReply(message, reply, ct);
    }
}

/// <summary>
///     Manages an RPC connection between two peers, allowing messages to be sent and received.
/// </summary>
/// <typeparam name="TS">The message type for sent messages</typeparam>
/// <typeparam name="TR">The message type for received messages</typeparam>
public class Speaker<TS, TR> : IAsyncDisposable
    where TS : RpcMessage<TS>, IMessage<TS>
    where TR : RpcMessage<TR>, IMessage<TR>, new()
{
    public delegate void OnErrorDelegate(Exception e);

    public delegate void OnReceiveDelegate(ReplyableRpcMessage<TS, TR> message);

    private readonly Stream _conn;

    // _cts is cancelled when Dispose is called and will cause all ongoing I/O
    // operations to be cancelled.
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<TR>> _pendingReplies = new();
    private readonly Serdes<TS, TR> _serdes = new();

    // _lastMessageId is incremented using an atomic operation, and as such the
    // first message ID will actually be 1.
    private ulong _lastMessageId;
    private Task? _receiveTask;

    /// <summary>
    ///     Event that is triggered when an error occurs. The handling code should dispose the Speaker after this event is
    ///     triggered.
    /// </summary>
    public event OnErrorDelegate? Error;

    /// <summary>
    ///     Event that is triggered when a message is received.
    /// </summary>
    public event OnReceiveDelegate? Receive;

    /// <summary>
    ///     Instantiates a speaker. The speaker will not perform any I/O until <c>StartAsync</c> is called.
    /// </summary>
    /// <param name="conn">Stream to use for I/O</param>
    public Speaker(Stream conn)
    {
        _conn = conn;
    }

    public async ValueTask DisposeAsync()
    {
        Error = null;
        await _cts.CancelAsync();
        if (_receiveTask is not null) await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        await _conn.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Performs a handshake with the peer and starts the async receive loop. The caller should attach it's Receive and
    ///     Error event handlers before calling this method.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        // Handshakes should always finish quickly, so enforce a 5s timeout.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await PerformHandshake(ct);

        // Start ReceiveLoop in the background.
        _receiveTask = ReceiveLoop(_cts.Token);
        _ = _receiveTask.ContinueWith(t =>
        {
            if (t.IsFaulted) Error?.Invoke(t.Exception!);
        }, CancellationToken.None);
    }

    private async Task PerformHandshake(CancellationToken ct = default)
    {
        // Simultaneously write the header string and read the header string in
        // case the conn is not buffered.
        var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var writeTask = WriteHeader(headerCts.Token);
        var readTask = ReadHeader(headerCts.Token);
        await TaskUtilities.CancellableWhenAll(headerCts, writeTask, readTask);

        var header = RpcHeader.Parse(await readTask);
        var expectedRole = RpcMessage<TR>.GetRole();
        if (header.Role != expectedRole)
            throw new ArgumentException($"Expected peer role '{expectedRole}' but got '{header.Role}'");

        header.Version.Validate(ApiVersion.Current);
    }

    private async Task WriteHeader(CancellationToken ct = default)
    {
        var header = new RpcHeader(RpcMessage<TS>.GetRole(), ApiVersion.Current);
        await _conn.WriteAsync(header.ToBytes(), ct);
    }

    private async Task<string> ReadHeader(CancellationToken ct = default)
    {
        var buf = new byte[256];
        var have = 0;

        while (true)
        {
            // Read into buf[have:have+1]
            await _conn.ReadExactlyAsync(buf, have, 1, ct);
            if (buf[have] == '\n') break;
            have++;
            if (have >= buf.Length)
                throw new IOException($"Header malformed or too large: '{Encoding.UTF8.GetString(buf)}'");
        }

        return Encoding.UTF8.GetString(buf, 0, have);
    }

    private async Task ReceiveLoop(CancellationToken ct = default)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await _serdes.ReadMessage(_conn, ct);
                if (message.RpcField.ResponseTo != 0)
                    // Look up the TaskCompletionSource for the message ID and
                    // complete it with the message.
                    if (_pendingReplies.TryRemove(message.RpcField.ResponseTo, out var tcs))
                        tcs.SetResult(message);

                // TODO: we should log unknown replies
                // Start a new task in the background to handle the message.
                _ = Task.Run(() => Receive?.Invoke(new ReplyableRpcMessage<TS, TR>(this, message)), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore, this is expected when being disposed.
        }
        catch (Exception e)
        {
            if (!ct.IsCancellationRequested) Error?.Invoke(e);
        }
    }

    /// <summary>
    ///     Send a message without waiting for a reply. If a reply is received it will be handled by the callback.
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="ct">Optional cancellation token</param>
    public async Task SendMessage(TS message, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        message.RpcField = new RPC
        {
            MsgId = Interlocked.Add(ref _lastMessageId, 1),
            ResponseTo = 0,
        };
        await _serdes.WriteMessage(_conn, message, cts.Token);
    }

    /// <summary>
    ///     Send a message and wait for a reply. The reply will be returned and the callback will not be invoked as long as the
    ///     reply is received before cancellation.
    /// </summary>
    /// <param name="message">Message to send - the Rpc field will be overwritten</param>
    /// <param name="ct">Optional cancellation token</param>
    /// <returns>Received reply</returns>
    public async ValueTask<TR> SendMessageAwaitReply(TS message, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        message.RpcField = new RPC
        {
            MsgId = Interlocked.Add(ref _lastMessageId, 1),
            ResponseTo = 0,
        };

        // Configure a TaskCompletionSource to complete when the reply is
        // received.
        var tcs = new TaskCompletionSource<TR>();
        _pendingReplies[message.RpcField.MsgId] = tcs;
        try
        {
            await _serdes.WriteMessage(_conn, message, cts.Token);
            // Wait for the reply to be received.
            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            // Clean up the pending reply if it was not received before
            // cancellation or another exception occurred.
            _pendingReplies.TryRemove(message.RpcField.MsgId, out _);
        }
    }

    /// <summary>
    ///     Sends a reply to a received message.
    /// </summary>
    /// <param name="originalMessage">Message to reply to - the Rpc field will be overwritten</param>
    /// <param name="reply">Reply message</param>
    /// <param name="ct">Optional cancellation token</param>
    public async Task SendReply(TR originalMessage, TS reply, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        reply.RpcField = new RPC
        {
            MsgId = Interlocked.Add(ref _lastMessageId, 1),
            ResponseTo = originalMessage.RpcField.MsgId,
        };
        await _serdes.WriteMessage(_conn, reply, cts.Token);
    }
}

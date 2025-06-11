using System.Collections.Concurrent;
using System.Text;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Utilities;
using Google.Protobuf;

namespace Coder.Desktop.Vpn;

/// <summary>
///     Thrown when the two peers are incompatible with each other.
/// </summary>
public class RpcVersionCompatibilityException : Exception
{
    public RpcVersionCompatibilityException(RpcVersionList localVersion, RpcVersionList remoteVersion) : base(
        $"No RPC versions are compatible: local={localVersion}, remote={remoteVersion}")
    {
    }
}

/// <summary>
///     Wraps a <c>RpcMessage</c> to allow easily sending a reply via the <c>Speaker</c>.
/// </summary>
public class ReplyableRpcMessage<TS, TR> : RpcMessage<TR>
    where TS : RpcMessage<TS>, IRpcMessageCompatibleWith<TR>, IMessage<TS>
    where TR : RpcMessage<TR>, IRpcMessageCompatibleWith<TS>, IMessage<TR>, new()
{
    private readonly TR _message;
    private readonly Speaker<TS, TR> _speaker;

    public override RPC? RpcField
    {
        get => _message.RpcField;
        set => _message.RpcField = value;
    }

    public override TR Message => _message;

    /// <param name="speaker">Speaker to use for sending reply</param>
    /// <param name="message">Original received message</param>
    public ReplyableRpcMessage(Speaker<TS, TR> speaker, TR message)
    {
        _speaker = speaker;
        _message = message;
    }

    public override void Validate()
    {
        _message.Validate();
    }

    /// <summary>
    ///     Sends a reply to the original message.
    /// </summary>
    /// <param name="reply">Correct reply message</param>
    /// <param name="ct">Optional cancellation token</param>
    public async Task SendReply(TS reply, CancellationToken ct = default)
    {
        await _speaker.SendReply(_message, reply, ct);
    }
}

/// <summary>
///     Manages an RPC connection between two peers, allowing messages to be sent and received.
/// </summary>
/// <typeparam name="TS">The message type for sent messages</typeparam>
/// <typeparam name="TR">The message type for received messages</typeparam>
public class Speaker<TS, TR> : IAsyncDisposable
    where TS : RpcMessage<TS>, IRpcMessageCompatibleWith<TR>, IMessage<TS>
    where TR : RpcMessage<TR>, IRpcMessageCompatibleWith<TS>, IMessage<TR>, new()
{
    public delegate void OnErrorDelegate(Exception e);

    public delegate void OnReceiveDelegate(ReplyableRpcMessage<TS, TR> message);

    /// <summary>
    ///     Event that is triggered when an error occurs. The handling code should dispose the Speaker after this event is
    ///     triggered.
    /// </summary>
    public event OnErrorDelegate? Error;

    /// <summary>
    ///     Event that is triggered when a message is received.
    /// </summary>
    public event OnReceiveDelegate? Receive;

    private readonly Stream _conn;

    // _cts is cancelled when Dispose is called and will cause all ongoing I/O
    // operations to be cancelled.
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<TR>> _pendingReplies = new();
    private readonly Serdes<TS, TR> _serdes = new();

    // _lastRequestId is incremented using an atomic operation, and as such the
    // first request ID will actually be 1.
    private ulong _lastRequestId;
    private Task? _receiveTask;

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
        await PerformHandshake(cts.Token);

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

        var headerStr = await readTask;
        RpcHeader header;
        try
        {
            header = RpcHeader.Parse(headerStr);
        }
        catch (Exception e)
        {
            throw new ArgumentException($"Failed to parse peer header '{headerStr}'", e);
        }

        var expectedRole = RpcMessage<TR>.GetRole();
        if (header.Role != expectedRole)
            throw new ArgumentException($"Expected peer role '{expectedRole}' but got '{header.Role}'");

        if (header.VersionList.IsCompatibleWith(RpcVersionList.Current) is null)
            throw new RpcVersionCompatibilityException(RpcVersionList.Current, header.VersionList);
    }

    private async Task WriteHeader(CancellationToken ct = default)
    {
        var header = new RpcHeader(RpcMessage<TS>.GetRole(), RpcVersionList.Current);
        var bytes = header.ToBytes();
        if (bytes.Length > 255)
            throw new ArgumentException($"Outgoing header too large: '{header}'");
        await _conn.WriteAsync(bytes, ct);
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
                if (message is { RpcField.ResponseTo: not 0 })
                {
                    // Look up the TaskCompletionSource for the message ID and
                    // complete it with the message.
                    if (_pendingReplies.TryRemove(message.RpcField.ResponseTo, out var tcs))
                        tcs.SetResult(message);
                    // TODO: we should log unknown replies
                    continue;
                }

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
    ///     Send a message that does not expect a reply.
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="ct">Optional cancellation token</param>
    public async Task SendMessage(TS message, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        message.RpcField = null;
        await _serdes.WriteMessage(_conn, message, cts.Token);
    }

    /// <summary>
    ///     Send a message and wait for a reply. The reply will be returned and the callback will not be invoked as long as the
    ///     reply is received before cancellation.
    /// </summary>
    /// <param name="message">Message to send - the Rpc field will be overwritten</param>
    /// <param name="ct">Optional cancellation token</param>
    /// <returns>Received reply</returns>
    public async ValueTask<TR> SendRequestAwaitReply(TS message, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        message.RpcField = new RPC
        {
            MsgId = Interlocked.Add(ref _lastRequestId, 1),
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
    /// <exception cref="ArgumentException">The original message is not a request and cannot be replied to</exception>
    public async Task SendReply(TR originalMessage, TS reply, CancellationToken ct = default)
    {
        if (originalMessage.RpcField == null || originalMessage.RpcField.MsgId == 0)
            throw new ArgumentException("Original message is not a request");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        reply.RpcField = new RPC
        {
            MsgId = 0,
            ResponseTo = originalMessage.RpcField.MsgId,
        };
        await _serdes.WriteMessage(_conn, reply, cts.Token);
    }
}

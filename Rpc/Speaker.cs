using System.Collections.Concurrent;
using System.Text;
using Coder.Desktop.Rpc.Proto;
using Google.Protobuf;

namespace Coder.Desktop.Rpc;

/// <summary>
///     Represents a role that either side of the connection can fulfil.
/// </summary>
public class SpeakerRole
{
    private const string ManagerString = "manager";
    private const string TunnelString = "tunnel";

    public static readonly SpeakerRole Manager = new(ManagerString);
    public static readonly SpeakerRole Tunnel = new(TunnelString);

    // TODO: it would be nice if we could expose this on the RpcMessage types instead
    public SpeakerRole(string role)
    {
        if (role != ManagerString && role != TunnelString) throw new ArgumentException($"Unknown role '{role}'");

        Role = role;
    }

    public string Role { get; }

    public override string ToString()
    {
        return Role;
    }

    #region SpeakerRole equality

    public static bool operator ==(SpeakerRole a, SpeakerRole b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(SpeakerRole a, SpeakerRole b)
    {
        return !a.Equals(b);
    }

    private bool Equals(SpeakerRole other)
    {
        return Role == other.Role;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((SpeakerRole)obj);
    }

    public override int GetHashCode()
    {
        return Role.GetHashCode();
    }

    #endregion
}

/// <summary>
///     A header to write or read from a stream to identify the speaker's role and version.
/// </summary>
/// <param name="role">Role of the speaker</param>
/// <param name="version">Version of the speaker</param>
public class SpeakerHeader(SpeakerRole role, ApiVersion version)
{
    public const string Preamble = "codervpn";

    public SpeakerRole Role { get; } = role;
    public ApiVersion Version { get; } = version;

    /// <summary>
    ///     Parse a header string into a <c>SpeakerHeader</c>.
    /// </summary>
    /// <param name="header">Raw header string without trailing newline</param>
    /// <returns>Parsed header</returns>
    /// <exception cref="ArgumentException">Invalid header string</exception>
    public static SpeakerHeader Parse(string header)
    {
        var parts = header.Split(' ');
        if (parts.Length != 3) throw new ArgumentException($"Wrong number of parts in header string '{header}'");
        if (parts[0] != Preamble) throw new ArgumentException($"Invalid preamble in header string '{header}'");

        var version = ApiVersion.ParseString(parts[1]);
        var role = new SpeakerRole(parts[2]);
        return new SpeakerHeader(role, version);
    }

    /// <summary>
    ///     Construct a header string from the role and version with a trailing newline.
    /// </summary>
    public override string ToString()
    {
        return $"{Preamble} {Version} {Role}\n";
    }

    public ReadOnlyMemory<byte> ToBytes()
    {
        return Encoding.UTF8.GetBytes(ToString());
    }
}

/// <summary>
///     Wraps a <c>RpcMessage</c> to allow easily sending a reply via the <c>Speaker</c>.
/// </summary>
/// <param name="speaker">Speaker to use for sending reply</param>
/// <param name="message">Original received message</param>
public class ReplyableRpcMessage<TSi, TS, TRi, TR>(Speaker<TSi, TS, TRi, TR> speaker, TR message) : RpcMessage<TRi>
    where TSi : IMessage<TSi>
    where TS : RpcMessage<TSi>
    where TRi : class, IMessage<TRi>, new()
    where TR : RpcMessage<TRi>
{
    public override RPC Rpc
    {
        get => message.Rpc;
        set => message.Rpc = value;
    }

    public override TRi Message
    {
        get => message.Message;
        set => message.Message = value;
    }

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
/// <typeparam name="TSi">The inner message type for sent messages</typeparam>
/// <typeparam name="TS">The wrapped message type for sent messages</typeparam>
/// <typeparam name="TRi">The inner message type for received messages</typeparam>
/// <typeparam name="TR">The wrapped message type for received messages</typeparam>
public class Speaker<TSi, TS, TRi, TR> : IDisposable, IAsyncDisposable
// TODO: it would be nice if this could just be the inner or wrapped types instead
    where TSi : IMessage<TSi>
    where TS : RpcMessage<TSi>
    where TRi : class, IMessage<TRi>, new()
    where TR : RpcMessage<TRi>
{
    public delegate void OnErrorDelegate(Exception e);

    public delegate void OnReceiveDelegate(ReplyableRpcMessage<TSi, TS, TRi, TR> message);

    private readonly Stream _conn;

    // _cts is cancelled when Dispose is called and will cause all ongoing I/O
    // operations to be cancelled.
    private readonly CancellationTokenSource _cts = new();
    private readonly SpeakerRole _me;

    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<TR>> _pendingReplies = new();
    private readonly Task _receiveTask;
    private readonly Serdes<TSi, TS, TRi, TR> _serdes = new();
    private readonly SpeakerRole _them;

    // _lastMessageId is incremented using an atomic operation, and as such the
    // first message ID will actually be 1.
    private ulong _lastMessageId;

    /// <summary>
    ///     Instantiates a speaker, performs a handshake with the peer, and starts receiving messages.
    /// </summary>
    /// <param name="conn">Stream to use for I/O - will be automatically closed on ctor failure or Dispose</param>
    /// <param name="me">The local role</param>
    /// <param name="them">The remote role</param>
    /// <param name="onReceive">Callback to fire on received messages (except replies)</param>
    /// <param name="onError">Callback to fire on fatal receive errors</param>
    /// <exception cref="TimeoutException">Could not complete handshake within 5s</exception>
    /// <exception cref="AggregateException">Handshake failed</exception>
    public Speaker(Stream conn, SpeakerRole me, SpeakerRole them, OnReceiveDelegate onReceive, OnErrorDelegate onError)
    {
        _conn = conn;
        _me = me;
        _them = them;
        Receive += onReceive;
        Error += onError;

        // Handshake with a hard timeout of 5s.
        var handshakeTask = PerformHandshake();
        handshakeTask.Wait(TimeSpan.FromSeconds(5));
        if (!handshakeTask.IsCompleted)
        {
            _conn.Dispose();
            throw new TimeoutException("RPC handshake timed out");
        }

        if (handshakeTask.IsFaulted)
        {
            _conn.Dispose();
            throw handshakeTask.Exception!;
        }

        _receiveTask = ReceiveLoop(_cts.Token);
        _receiveTask.ContinueWith(t =>
        {
            if (t.IsFaulted) Error.Invoke(t.Exception!);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        await _conn.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        _cts.Cancel();
        // Wait up to 5s for _receiveTask to finish, we don't really care about
        // the result.
        _receiveTask.Wait(TimeSpan.FromSeconds(5));
        _conn.Dispose();
        GC.SuppressFinalize(this);
    }

    // TODO: do we want to do events API or channels API?
    private event OnReceiveDelegate Receive;
    private event OnErrorDelegate Error;

    private async Task PerformHandshake(CancellationToken ct = default)
    {
        // Simultaneously write the header string and read the header string in
        // case the conn is not buffered.
        var writeTask = WriteHeader(ct);
        var readTask = ReadHeader(ct);
        await Task.WhenAll(writeTask, readTask);

        var header = SpeakerHeader.Parse(await readTask);
        if (header.Role != _them) throw new ArgumentException($"Expected peer role '{_them}' but got '{header.Role}'");

        header.Version.Validate(ApiVersion.Current);
    }

    private async Task WriteHeader(CancellationToken ct = default)
    {
        var header = new SpeakerHeader(_me, ApiVersion.Current);
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
                if (message.Rpc.ResponseTo != 0)
                    // Look up the TaskCompletionSource for the message ID and
                    // complete it with the message.
                    if (_pendingReplies.TryRemove(message.Rpc.ResponseTo, out var tcs))
                        tcs.SetResult(message);

                // TODO: we should log unknown replies
                // Start a new task in the background to handle the message.
                _ = Task.Run(() => Receive.Invoke(new ReplyableRpcMessage<TSi, TS, TRi, TR>(this, message)), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore, this is expected when being disposed.
        }
        catch (Exception e)
        {
            if (!ct.IsCancellationRequested) Error.Invoke(e);
        }
    }

    /// <summary>
    ///     Send a message without waiting for a reply. If a reply is received it will be handled by the callback.
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="ct">Optional cancellation token</param>
    public async Task SendMessage(TS message, CancellationToken ct = default)
    {
        message.Rpc = new RPC
        {
            MsgId = Interlocked.Add(ref _lastMessageId, 1),
            ResponseTo = 0,
        };
        await _serdes.WriteMessage(_conn, message, ct);
    }

    /// <summary>
    ///     Send a message and wait for a reply. The reply will be returned and the callback will not be invoked as long as the
    ///     reply is received before cancellation.
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="ct">Optional cancellation token</param>
    /// <returns>Received reply</returns>
    public async ValueTask<TR> SendMessageAwaitReply(TS message, CancellationToken ct = default)
    {
        message.Rpc = new RPC
        {
            MsgId = Interlocked.Add(ref _lastMessageId, 1),
            ResponseTo = 0,
        };

        // Configure a TaskCompletionSource to complete when the reply is
        // received.
        var tcs = new TaskCompletionSource<TR>();
        _pendingReplies[message.Rpc.MsgId] = tcs;
        try
        {
            await _serdes.WriteMessage(_conn, message, ct);
            // Wait for the reply to be received.
            return await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            // Clean up the pending reply if it was not received before
            // cancellation.
            _pendingReplies.TryRemove(message.Rpc.MsgId, out _);
        }
    }

    /// <summary>
    ///     Sends a reply to a received request.
    /// </summary>
    /// <param name="originalMessage">Message to reply to</param>
    /// <param name="reply">Reply message</param>
    /// <param name="ct">Optional cancellation token</param>
    public async Task SendReply(TR originalMessage, TS reply, CancellationToken ct = default)
    {
        reply.Rpc = new RPC
        {
            MsgId = Interlocked.Add(ref _lastMessageId, 1),
            ResponseTo = originalMessage.Rpc.MsgId,
        };
        await _serdes.WriteMessage(_conn, reply, ct);
    }
}
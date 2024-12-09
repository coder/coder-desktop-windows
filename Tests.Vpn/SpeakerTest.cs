using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using Coder.Desktop.Vpn;
using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.Tests.Vpn;

#region BidrectionalPipe

internal class BidirectionalPipe(PipeReader reader, PipeWriter writer) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => -1;

    public override long Position
    {
        get => -1;
        set => throw new NotImplementedException("BidirectionalPipe does not support setting position");
    }

    public static (BidirectionalPipe, BidirectionalPipe) New()
    {
        var pipe1 = new Pipe();
        var pipe2 = new Pipe();
        return (new BidirectionalPipe(pipe1.Reader, pipe2.Writer), new BidirectionalPipe(pipe2.Reader, pipe1.Writer));
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var result = await reader.ReadAtLeastAsync(1, ct);
        var n = Math.Min((int)result.Buffer.Length, count);
        // Copy result.Buffer[0:n] to buffer[offset:offset+n]
        result.Buffer.Slice(0, n).CopyTo(buffer.AsMemory(offset, n).Span);
        if (!result.IsCompleted) reader.AdvanceTo(result.Buffer.GetPosition(n));
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException("BidirectionalPipe does not support seeking");
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException("BidirectionalPipe does not support setting length");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        await writer.WriteAsync(buffer.AsMemory(offset, count), ct);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        writer.Complete();
        reader.Complete();
    }
}

#endregion

#region FailableStream

internal class FailableStream : Stream
{
    private readonly Stream _inner;
    private readonly TaskCompletionSource _readTcs = new();

    private readonly TaskCompletionSource _writeTcs = new();

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public FailableStream(Stream inner, Exception? writeException, Exception? readException)
    {
        _inner = inner;
        if (writeException != null) _writeTcs.SetException(writeException);
        if (readException != null) _readTcs.SetException(readException);
    }

    public void SetWriteException(Exception ex)
    {
        _writeTcs.SetException(ex);
    }

    public void SetReadException(Exception ex)
    {
        _readTcs.SetException(ex);
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _inner.SetLength(value);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _inner.ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    private void CheckException(TaskCompletionSource tcs)
    {
        if (tcs.Task.IsFaulted) throw tcs.Task.Exception.InnerException!;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        CheckException(_readTcs);
        var readTask = _inner.ReadAsync(buffer, cancellationToken);
        await Task.WhenAny(readTask.AsTask(), _readTcs.Task);
        CheckException(_readTcs);
        return await readTask;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.WriteAsync(buffer, offset, count).Wait();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        CheckException(_writeTcs);
        var writeTask = _inner.WriteAsync(buffer, cancellationToken);
        await Task.WhenAny(writeTask.AsTask(), _writeTcs.Task);
        CheckException(_writeTcs);
        await writeTask;
    }
}

#endregion

[TestFixture]
public class SpeakerTest
{
    [Test(Description = "Send a message from speaker1 to speaker2, receive it, and send a reply back")]
    [CancelAfter(30_000)]
    public async Task SendReceiveReplyReceive(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.New();

        await using var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(stream1);
        var speaker1Ch = Channel
            .CreateUnbounded<ReplyableRpcMessage<ManagerMessage, TunnelMessage>>();
        speaker1.Receive += msg => { Assert.That(speaker1Ch.Writer.TryWrite(msg), Is.True); };
        speaker1.Error += ex => { Assert.Fail($"speaker1 error: {ex}"); };

        await using var speaker2 = new Speaker<TunnelMessage, ManagerMessage>(stream2);
        var speaker2Ch = Channel
            .CreateUnbounded<ReplyableRpcMessage<TunnelMessage, ManagerMessage>>();
        speaker2.Receive += msg => { Assert.That(speaker2Ch.Writer.TryWrite(msg), Is.True); };
        speaker2.Error += ex => { Assert.Fail($"speaker2 error: {ex}"); };

        // Start both speakers simultaneously
        await Task.WhenAll(speaker1.StartAsync(ct), speaker2.StartAsync(ct));

        // Send a normal message from speaker2 to speaker1
        await speaker2.SendMessage(new TunnelMessage
        {
            PeerUpdate = new PeerUpdate(),
        }, ct);
        var receivedMessage = await speaker1Ch.Reader.ReadAsync(ct);
        Assert.That(receivedMessage.RpcField, Is.Null); // not a request
        Assert.That(receivedMessage.Message.PeerUpdate, Is.Not.Null);

        // Send a message from speaker1 to speaker2 in the background
        var sendTask = speaker1.SendRequestAwaitReply(new ManagerMessage
        {
            Start = new StartRequest
            {
                ApiToken = "test",
                CoderUrl = "test",
            },
        }, ct);

        // Receive the message in speaker2
        var message = await speaker2Ch.Reader.ReadAsync(ct);
        Assert.That(message.RpcField, Is.Not.Null);
        Assert.That(message.RpcField!.MsgId, Is.Not.EqualTo(0));
        Assert.That(message.RpcField!.ResponseTo, Is.EqualTo(0));
        Assert.That(message.Message.Start.ApiToken, Is.EqualTo("test"));

        // Send a reply back to speaker1
        await message.SendReply(new TunnelMessage
        {
            Start = new StartResponse
            {
                Success = true,
            },
        }, ct);

        // Receive the reply in speaker1 by awaiting sendTask
        var reply = await sendTask;
        Assert.That(message.RpcField, Is.Not.Null);
        Assert.That(reply.RpcField!.MsgId, Is.EqualTo(0));
        Assert.That(reply.RpcField!.ResponseTo, Is.EqualTo(message.RpcField!.MsgId));
        Assert.That(reply.Message.Start.Success, Is.True);
    }

    [Test(Description = "Encounter a write error during handshake")]
    [CancelAfter(30_000)]
    public async Task WriteError(CancellationToken ct)
    {
        var (stream1, _) = BidirectionalPipe.New();
        var writeEx = new IOException("Test write error");
        var failStream = new FailableStream(stream1, writeEx, null);

        await using var speaker = new Speaker<ManagerMessage, TunnelMessage>(failStream);

        var gotEx = Assert.ThrowsAsync<IOException>(() => speaker.StartAsync(ct));
        Assert.That(gotEx, Is.EqualTo(writeEx));
    }

    [Test(Description = "Encounter a read error during handshake")]
    [CancelAfter(30_000)]
    public async Task ReadError(CancellationToken ct)
    {
        var (stream1, _) = BidirectionalPipe.New();
        var readEx = new IOException("Test read error");
        var failStream = new FailableStream(stream1, null, readEx);

        await using var speaker = new Speaker<ManagerMessage, TunnelMessage>(failStream);

        var gotEx = Assert.ThrowsAsync<IOException>(() => speaker.StartAsync(ct));
        Assert.That(gotEx, Is.EqualTo(readEx));
    }

    [Test(Description = "Receive a header that exceeds 256 bytes")]
    [CancelAfter(30_000)]
    public async Task ReadLargeHeader(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.New();
        await using var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(stream1);

        var header = new byte[257];
        for (var i = 0; i < header.Length; i++) header[i] = (byte)'a';
        await stream2.WriteAsync(header, ct);

        var gotEx = Assert.ThrowsAsync<IOException>(() => speaker1.StartAsync(ct));
        Assert.That(gotEx.Message, Does.Contain("Header malformed or too large"));
    }

    [Test(Description = "Receive an invalid header")]
    [CancelAfter(30_000)]
    public async Task ReceiveInvalidHeader(CancellationToken ct)
    {
        var cases = new Dictionary<string, (string, string?)>
        {
            { "invalid\n", ("Failed to parse peer header", "Wrong number of parts in header string") },
            { "cats tunnel 1.0\n", ("Failed to parse peer header", "Invalid preamble in header string") },
            { "codervpn cats 1.0\n", ("Failed to parse peer header", "Unknown role 'cats'") },
            { "codervpn manager 1.0\n", ("Expected peer role 'tunnel' but got 'manager'", null) },
            {
                "codervpn tunnel 1000.1\n",
                ($"No RPC versions are compatible: local={RpcVersionList.Current}, remote=1000.1", null)
            },
            { "codervpn tunnel 0.1\n", ("Failed to parse peer header", "Invalid version list '0.1'") },
            { "codervpn tunnel 1.0,1.2\n", ("Failed to parse peer header", "Invalid version list '1.0,1.2'") },
            { "codervpn tunnel 2.0,3.1,1.2\n", ("Failed to parse peer header", "Invalid version list '2.0,3.1,1.2'") },
        };

        foreach (var (header, (expectedOuter, expectedInner)) in cases)
        {
            var (stream1, stream2) = BidirectionalPipe.New();
            await using var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(stream1);

            await stream2.WriteAsync(Encoding.UTF8.GetBytes(header), ct);

            var gotEx = Assert.CatchAsync(() => speaker1.StartAsync(ct), $"header: '{header}'");
            Assert.That(gotEx.Message, Does.Contain(expectedOuter), $"header: '{header}'");
            if (expectedInner is null)
            {
                Assert.That(gotEx.InnerException, Is.Null, $"header: '{header}'");
                continue;
            }

            Assert.That(gotEx.InnerException, Is.Not.Null, $"header: '{header}'");
            Assert.That(gotEx.InnerException!.Message, Does.Contain(expectedInner), $"header: '{header}'");
        }
    }

    [Test(Description = "Encounter a write error during message send")]
    [CancelAfter(30_000)]
    public async Task SendMessageWriteError(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.New();
        var failStream = new FailableStream(stream1, null, null);

        await using var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(failStream);
        speaker1.Receive += msg => Assert.Fail($"speaker1 received message: {msg}");
        speaker1.Error += ex => Assert.Fail($"speaker1 error: {ex}");
        await using var speaker2 = new Speaker<TunnelMessage, ManagerMessage>(stream2);
        speaker2.Receive += msg => Assert.Fail($"speaker2 received message: {msg}");
        speaker2.Error += ex => Assert.Fail($"speaker2 error: {ex}");
        await Task.WhenAll(speaker1.StartAsync(ct), speaker2.StartAsync(ct));

        var writeEx = new IOException("Test write error");
        failStream.SetWriteException(writeEx);

        var gotEx = Assert.ThrowsAsync<IOException>(() => speaker1.SendMessage(new ManagerMessage
        {
            Start = new StartRequest(),
        }, ct));
        Assert.That(gotEx, Is.EqualTo(writeEx));
    }

    [Test(Description = "Encounter a read error during message receive")]
    [CancelAfter(30_000)]
    public async Task ReceiveMessageReadError(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.New();
        var failStream = new FailableStream(stream1, null, null);

        // Speaker1 is bound to failStream and will write an error to errorCh
        var errorCh = Channel.CreateUnbounded<Exception>();
        await using var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(failStream);
        speaker1.Receive += msg => Assert.Fail($"speaker1 received message: {msg}");
        speaker1.Error += ex => errorCh.Writer.TryWrite(ex);

        // Speaker2 is normal and is only used to perform a handshake
        await using var speaker2 = new Speaker<TunnelMessage, ManagerMessage>(stream2);
        speaker2.Receive += msg => Assert.Fail($"speaker2 received message: {msg}");
        speaker2.Error += ex => Assert.Fail($"speaker2 error: {ex}");
        await Task.WhenAll(speaker1.StartAsync(ct), speaker2.StartAsync(ct));

        // Now the handshake is complete, cause all reads to fail
        var readEx = new IOException("Test write error");
        failStream.SetReadException(readEx);

        var gotEx = await errorCh.Reader.ReadAsync(ct);
        Assert.That(gotEx, Is.EqualTo(readEx));

        // The receive loop should be stopped within a timely fashion.
        var receiveLoopTask = (Task?)speaker1.GetType()
            .GetField("_receiveTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(speaker1);
        if (receiveLoopTask is null)
        {
            Assert.Fail("Receive loop task not found");
        }
        else
        {
            var delayTask = Task.Delay(TimeSpan.FromSeconds(5), ct);
            await Task.WhenAny(receiveLoopTask, delayTask);
            Assert.That(receiveLoopTask.IsCompleted, Is.True);
        }
    }

    [Test(Description = "Handle dispose while receive loop is running")]
    [CancelAfter(30_000)]
    public async Task DisposeWhileReceiveLoopRunning(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.New();
        var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(stream1);
        await using var speaker2 = new Speaker<TunnelMessage, ManagerMessage>(stream2);
        await Task.WhenAll(speaker1.StartAsync(ct), speaker2.StartAsync(ct));

        // Dispose should happen in a timely fashion
        var disposeTask = speaker1.DisposeAsync();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(5), ct);
        await Task.WhenAny(disposeTask.AsTask(), delayTask);
        Assert.That(disposeTask.IsCompleted, Is.True);

        // Receive loop should be stopped
        var receiveLoopTask = (Task?)speaker1.GetType()
            .GetField("_receiveTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(speaker1);
        if (receiveLoopTask is null)
            Assert.Fail("Receive loop task not found");
        else
            Assert.That(receiveLoopTask.IsCompleted, Is.True);
    }

    [Test(Description = "Handle dispose while a message is awaiting a reply")]
    [CancelAfter(30_000)]
    public async Task DisposeWhileAwaitingReply(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.New();
        var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(stream1);
        await using var speaker2 = new Speaker<TunnelMessage, ManagerMessage>(stream2);
        await Task.WhenAll(speaker1.StartAsync(ct), speaker2.StartAsync(ct));

        // Send a message from speaker1 to speaker2
        var sendTask = speaker1.SendRequestAwaitReply(new ManagerMessage
        {
            Start = new StartRequest(),
        }, ct);

        // Dispose speaker1
        await speaker1.DisposeAsync();

        // The send task should complete with an exception
        Assert.ThrowsAsync<TaskCanceledException>(() => sendTask.AsTask());
    }
}

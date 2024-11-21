using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Coder.Desktop.Rpc;
using Coder.Desktop.Rpc.Proto;

namespace Coder.Desktop.Tests.Rpc;

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

public class SpeakerTest
{
    [Test]
    public async Task Ok()
    {
        var (stream1, stream2) = BidirectionalPipe.New();

        var speaker1Ch = Channel
            .CreateUnbounded<ReplyableRpcMessage<ManagerMessage, ManagerMessageWrapper, TunnelMessage,
                TunnelMessageWrapper>>();
        var speaker2Ch = Channel
            .CreateUnbounded<ReplyableRpcMessage<TunnelMessage, TunnelMessageWrapper, ManagerMessage,
                ManagerMessageWrapper>>();

        // Start two speakers asynchronously as startup is blocking.
        var speaker1Task = Task.Run(() =>
            new Speaker<ManagerMessage, ManagerMessageWrapper, TunnelMessage, TunnelMessageWrapper>(stream1,
                SpeakerRole.Manager, SpeakerRole.Tunnel,
                tri =>
                {
                    Console.WriteLine($"speaker1 received message: {tri.Rpc.MsgId}");
                    Assert.That(speaker1Ch.Writer.TryWrite(tri), Is.True);
                }, ex => { Assert.Fail($"speaker1 error: {ex}"); }));
        var speaker2Task = Task.Run(() =>
            new Speaker<TunnelMessage, TunnelMessageWrapper, ManagerMessage, ManagerMessageWrapper>(stream2,
                SpeakerRole.Tunnel, SpeakerRole.Manager,
                tri =>
                {
                    Console.WriteLine($"speaker2 received message: {tri.Rpc.MsgId}");
                    Assert.That(speaker2Ch.Writer.TryWrite(tri), Is.True);
                }, ex => { Assert.Fail($"speaker2 error: {ex}"); }));

        Task.WaitAll(speaker1Task, speaker2Task);
        await using var speaker1 = await speaker1Task;
        await using var speaker2 = await speaker2Task;

        var sendTask = speaker1.SendMessageAwaitReply(new ManagerMessageWrapper(new ManagerMessage
        {
            Start = new StartRequest
            {
                ApiToken = "test",
                CoderUrl = "test",
            },
        }));

        var message = await speaker2Ch.Reader.ReadAsync();
        Assert.That(message.Message.Start.ApiToken, Is.EqualTo("test"));
        await message.SendReply(new TunnelMessageWrapper(new TunnelMessage
        {
            Start = new StartResponse
            {
                Success = true,
            },
        }));

        var reply = await sendTask;
        Assert.That(reply.Message.Start.Success, Is.True);
    }
}
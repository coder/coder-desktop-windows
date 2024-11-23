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

        var speaker1 = new Speaker<ManagerMessage, TunnelMessage>(stream1);
        var speaker1Ch = Channel
            .CreateUnbounded<ReplyableRpcMessage<ManagerMessage, TunnelMessage>>();
        speaker1.Receive += msg =>
        {
            Console.WriteLine($"speaker1 received message: {msg.RpcField.MsgId}");
            Assert.That(speaker1Ch.Writer.TryWrite(msg), Is.True);
        };
        speaker1.Error += ex => { Assert.Fail($"speaker1 error: {ex}"); };

        var speaker2 = new Speaker<TunnelMessage, ManagerMessage>(stream2);
        var speaker2Ch = Channel
            .CreateUnbounded<ReplyableRpcMessage<TunnelMessage, ManagerMessage>>();
        speaker2.Receive += msg =>
        {
            Console.WriteLine($"speaker2 received message: {msg.RpcField.MsgId}");
            Assert.That(speaker2Ch.Writer.TryWrite(msg), Is.True);
        };
        speaker2.Error += ex => { Assert.Fail($"speaker2 error: {ex}"); };

        // Start both speakers simultaneously
        Task.WaitAll(speaker1.StartAsync(), speaker2.StartAsync());

        var sendTask = speaker1.SendMessageAwaitReply(new ManagerMessage
        {
            Start = new StartRequest
            {
                ApiToken = "test",
                CoderUrl = "test",
            },
        });

        var message = await speaker2Ch.Reader.ReadAsync();
        Assert.That(message.Message.Start.ApiToken, Is.EqualTo("test"));
        await message.SendReply(new TunnelMessage
        {
            Start = new StartResponse
            {
                Success = true,
            },
        });

        var reply = await sendTask;
        Assert.That(reply.Message.Start.Success, Is.True);
    }
}

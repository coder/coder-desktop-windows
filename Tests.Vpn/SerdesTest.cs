using System.Buffers.Binary;
using Coder.Desktop.Vpn;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Utilities;
using Google.Protobuf;

namespace Coder.Desktop.Tests.Vpn;

[TestFixture]
public class SerdesTest
{
    [Test(Description = "Tests that writing and reading a message works")]
    [CancelAfter(30_000)]
    public async Task WriteReadMessage(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.NewInMemory();
        var serdes = new Serdes<ManagerMessage, ManagerMessage>();

        var msg = new ManagerMessage
        {
            Start = new StartRequest(),
        };
        await serdes.WriteMessage(stream1, msg, ct);
        var got = await serdes.ReadMessage(stream2, ct);
        Assert.That(msg, Is.EqualTo(got));
    }

    [Test(Description = "Tests that writing a message larger than 16 MiB throws an exception")]
    [CancelAfter(30_000)]
    public void WriteMessageTooLarge(CancellationToken ct)
    {
        var (stream1, _) = BidirectionalPipe.NewInMemory();
        var serdes = new Serdes<ManagerMessage, ManagerMessage>();

        var msg = new ManagerMessage
        {
            Start = new StartRequest
            {
                ApiToken = new string('a', 0x1000001),
                CoderUrl = "test",
            },
        };
        Assert.ThrowsAsync<ArgumentException>(() => serdes.WriteMessage(stream1, msg, ct));
    }

    [Test(Description = "Tests that attempting to read a message larger than 16 MiB throws an exception")]
    [CancelAfter(30_000)]
    public async Task ReadMessageTooLarge(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.NewInMemory();
        var serdes = new Serdes<ManagerMessage, ManagerMessage>();

        // In this test we don't actually write a message as the parser should
        // bail out immediately after reading the message length
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, 0x1000001);
        await stream1.WriteAsync(lenBytes, ct);
        Assert.ThrowsAsync<IOException>(() => serdes.ReadMessage(stream2, ct));
    }

    [Test(Description = "Read an empty (size 0) message from the stream")]
    [CancelAfter(30_000)]
    public async Task ReadEmptyMessage(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.NewInMemory();
        var serdes = new Serdes<ManagerMessage, ManagerMessage>();

        // Write an empty message.
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, 0);
        await stream1.WriteAsync(lenBytes, ct);
        var ex = Assert.ThrowsAsync<IOException>(() => serdes.ReadMessage(stream2, ct));
        Assert.That(ex.Message, Does.Contain("Received message size 0"));
    }

    [Test(Description = "Read an invalid/corrupt message from the stream")]
    [CancelAfter(30_000)]
    public async Task ReadInvalidMessage(CancellationToken ct)
    {
        var (stream1, stream2) = BidirectionalPipe.NewInMemory();
        var serdes = new Serdes<ManagerMessage, ManagerMessage>();

        var lenBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, 1);
        await stream1.WriteAsync(lenBytes, ct);
        await stream1.WriteAsync(new byte[1], ct);
        var ex = Assert.ThrowsAsync<IOException>(() => serdes.ReadMessage(stream2, ct));
        Assert.That(ex.InnerException, Is.TypeOf(typeof(InvalidProtocolBufferException)));
    }
}

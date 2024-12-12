using System.IO.Pipelines;

namespace Coder.Desktop.Vpn.Utilities;

/// <summary>
///     BidirectionalPipe implements Stream using a read-only Stream and a write-only Stream.
/// </summary>
/// <param name="reader">The stream to perform reads from</param>
/// <param name="writer">The stream to write data to</param>
public class BidirectionalPipe(Stream reader, Stream writer) : Stream
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

    /// <summary>
    ///     Creates a new pair of BidirectionalPipes that are connected to each other using buffered in-memory pipes.
    /// </summary>
    /// <returns>Two pipes connected to each other</returns>
    public static (BidirectionalPipe, BidirectionalPipe) NewInMemory()
    {
        var pipe1 = new Pipe();
        var pipe2 = new Pipe();
        return (
            new BidirectionalPipe(pipe1.Reader.AsStream(), pipe2.Writer.AsStream()),
            new BidirectionalPipe(pipe2.Reader.AsStream(), pipe1.Writer.AsStream())
        );
    }

    public override void Flush()
    {
        writer.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return reader.Read(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
#pragma warning disable CA1835
        return await reader.ReadAsync(buffer, offset, count, ct);
#pragma warning restore CA1835
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return reader.ReadAsync(buffer, cancellationToken);
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
        writer.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
#pragma warning disable CA1835
        await writer.WriteAsync(buffer, offset, count, ct);
#pragma warning restore CA1835
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return writer.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        writer.Dispose();
        reader.Dispose();
    }
}

using System.Buffers.Binary;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Utilities;
using Google.Protobuf;

namespace Coder.Desktop.Vpn;

/// <summary>
///     Serdes provides serialization and deserialization of messages read from a Stream.
/// </summary>
public class Serdes<TS, TR>
    where TS : RpcMessage<TS>, IMessage<TS>
    where TR : RpcMessage<TR>, IMessage<TR>, new()
{
    private const int MaxMessageSize = 0x1000000; // 16MiB

    private readonly MessageParser<TR> _parser = new(() => new TR());

    private readonly RaiiSemaphoreSlim _readLock = new(1, 1);
    private readonly RaiiSemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    ///     Encodes and writes a message to the Stream. Only one message will be written at a time.
    /// </summary>
    /// <param name="conn">Stream to write the encoded message to</param>
    /// <param name="message">Message to encode and write</param>
    /// <param name="ct">Optional cancellation token</param>
    /// <exception cref="ArgumentException">If the message is invalid</exception>
    public async Task WriteMessage(Stream conn, TS message, CancellationToken ct = default)
    {
        message.Validate(); // throws ArgumentException if invalid
        using var _ = await _writeLock.LockAsync(ct);

        var mb = message.ToByteArray();
        if (mb == null || mb.Length == 0)
            throw new ArgumentException("Marshalled message is empty");
        if (mb.Length > MaxMessageSize)
            throw new ArgumentException($"Marshalled message size {mb.Length} exceeds maximum {MaxMessageSize}");

        var lenBytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)mb.Length);
        await conn.WriteAsync(lenBytes, ct);
        await conn.WriteAsync(mb, ct);
    }

    /// <summary>
    ///     Reads a decodes a single message from the stream. Only one message will be read at a time.
    /// </summary>
    /// <param name="conn">Stream to read the message from</param>
    /// <param name="ct">Optional cancellation token</param>
    /// <returns>Decoded message</returns>
    /// <exception cref="IOException">Could not decode the message</exception>
    /// <exception cref="ArgumentException">The message is invalid</exception>
    public async Task<TR> ReadMessage(Stream conn, CancellationToken ct = default)
    {
        using var _ = await _readLock.LockAsync(ct);

        var lenBytes = new byte[sizeof(uint)];
        await conn.ReadExactlyAsync(lenBytes, ct);
        var len = BinaryPrimitives.ReadUInt32BigEndian(lenBytes);
        if (len == 0)
            throw new IOException("Received message size 0");
        if (len > MaxMessageSize)
            throw new IOException($"Received message size {len} exceeds maximum {MaxMessageSize}");

        var msgBytes = new byte[len];
        await conn.ReadExactlyAsync(msgBytes, ct);

        try
        {
            var msg = _parser.ParseFrom(msgBytes);
            if (msg is null)
                throw new IOException("Parsed message is null");
            msg.Validate(); // throws ArgumentException if invalid
            return msg;
        }
        catch (Exception e)
        {
            throw new IOException("Failed to parse message", e);
        }
    }
}

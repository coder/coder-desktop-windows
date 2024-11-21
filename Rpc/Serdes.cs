using System.Buffers.Binary;
using Coder.Desktop.Rpc.Proto;
using Google.Protobuf;

namespace Coder.Desktop.Rpc;

/// <summary>
///     RaiiSemaphoreSlim is a wrapper around SemaphoreSlim that provides RAII-style locking.
/// </summary>
internal class RaiiSemaphoreSlim(int initialCount, int maxCount)
{
    private readonly SemaphoreSlim _semaphore = new(initialCount, maxCount);

    public async ValueTask<IDisposable> LockAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Lock(_semaphore);
    }

    private class Lock(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose()
        {
            semaphore.Release();
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
///     Serdes provides serialization and deserialization of messages read from a Stream.
/// </summary>
public class Serdes<TSi, TS, TRi, TR>
    where TSi : IMessage<TSi>
    where TS : RpcMessage<TSi>
    where TRi : class, IMessage<TRi>, new()
    where TR : RpcMessage<TRi>
{
    private const int MaxMessageSize = 0x1000000; // 16MiB

    private readonly MessageParser<TRi> _parser = new(() => new TRi());

    private readonly RaiiSemaphoreSlim _readLock = new(1, 1);
    private readonly RaiiSemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    ///     Encodes and writes a message to the Stream. Only one message will be written at a time.
    /// </summary>
    /// <param name="conn">Stream to write the encoded message to</param>
    /// <param name="message">Message to encode and write</param>
    /// <param name="ct">Optional cancellation token</param>
    /// <exception cref="ArgumentException">If the message exceeds the maximum message size of 16 MiB</exception>
    public async Task WriteMessage(Stream conn, TS message, CancellationToken ct = default)
    {
        using var _ = await _writeLock.LockAsync(ct);

        var mb = message.Message.ToByteArray();
        if (mb.Length > MaxMessageSize)
            throw new ArgumentException($"Marshalled message size {mb.Length} exceeds maximum {MaxMessageSize}");

        var lenBytes = new byte[4];
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
    /// <exception cref="InvalidOperationException">Could not cast the received message to the expected type</exception>
    public async Task<TR> ReadMessage(Stream conn, CancellationToken ct = default)
    {
        using var _ = await _readLock.LockAsync(ct);

        var lenBytes = new byte[sizeof(uint)];
        await conn.ReadExactlyAsync(lenBytes, ct);
        var len = BinaryPrimitives.ReadUInt32BigEndian(lenBytes);
        if (len > MaxMessageSize)
            throw new IOException($"Received message size {len} exceeds maximum {MaxMessageSize}");

        var msgBytes = new byte[len];
        await conn.ReadExactlyAsync(msgBytes, ct);

        var msg = _parser.ParseFrom(msgBytes);
        if (msg == null) throw new IOException("Failed to parse message");

        return msg.ToRpcMessage() as TR ?? throw new InvalidOperationException("Failed to cast message");
    }
}
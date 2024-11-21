using Google.Protobuf;

namespace Coder.Desktop.Rpc.Proto;

/// <summary>
///     Represents an actual over-the-wire message type.
/// </summary>
/// <typeparam name="T">Protobuf message type</typeparam>
public abstract class RpcMessage<T> where T : IMessage<T>
{
    /// <summary>
    ///     The inner RPC component of the message.
    /// </summary>
    public abstract RPC Rpc { get; set; }

    /// <summary>
    ///     The actual full message.
    /// </summary>
    public abstract T Message { get; set; }
}

/// <summary>
///     Wraps a protobuf <c>ManagerMessage</c> to implement <c>RpcMessage</c>.
/// </summary>
public class ManagerMessageWrapper(ManagerMessage message) : RpcMessage<ManagerMessage>
{
    public override RPC Rpc
    {
        get => Message.Rpc;
        set => Message.Rpc = value;
    }

    public override ManagerMessage Message { get; set; } = message;
}

/// <summary>
///     Wraps a protobuf <c>TunnelMessage</c> to implement <c>RpcMessage</c>.
/// </summary>
public class TunnelMessageWrapper(TunnelMessage message) : RpcMessage<TunnelMessage>
{
    public override RPC Rpc
    {
        get => Message.Rpc;
        set => Message.Rpc = value;
    }

    public override TunnelMessage Message { get; set; } = message;
}

/// <summary>
///     Provides extension methods for Protobuf messages.
/// </summary>
public static class ProtoWrappers
{
    /// <summary>
    ///     Attempts to convert a Protobuf message to an <c>RpcMessage</c>.
    /// </summary>
    /// <param name="message">Protobuf message</param>
    /// <typeparam name="T">Protobuf message type</typeparam>
    /// <returns>A wrapped message</returns>
    /// <exception cref="ArgumentException">Unknown message type</exception>
    public static RpcMessage<T> ToRpcMessage<T>(this IMessage<T> message) where T : IMessage<T>
    {
        return message switch
        {
            TunnelMessage tunnelMessage => (RpcMessage<T>)(object)new TunnelMessageWrapper(tunnelMessage),
            ManagerMessage managerMessage => (RpcMessage<T>)(object)new ManagerMessageWrapper(managerMessage),
            _ => throw new ArgumentException($"Unknown message type {message.GetType()}"),
        };
    }
}
using Google.Protobuf;

namespace Coder.Desktop.Rpc.Proto;

/// <summary>
///     Represents an actual over-the-wire message type.
/// </summary>
/// <typeparam name="T">Protobuf message type</typeparam>
public abstract class RpcMessage<T> where T : IMessage<T>
{
    /// <summary>
    ///     The inner RPC component of the message. This is a separate field as the C# compiler does not allow the existing Rpc
    ///     field to be overridden or implement this abstract property.
    /// </summary>
    public abstract RPC RpcField { get; set; }

    /// <summary>
    ///     The inner message component of the message. This exists so values of type RpcMessage can easily get message
    ///     contents.
    /// </summary>
    public abstract T Message { get; }
}

public partial class ManagerMessage : RpcMessage<ManagerMessage>
{
    public override RPC RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override ManagerMessage Message => this;
}

public partial class TunnelMessage : RpcMessage<TunnelMessage>
{
    public override RPC RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override TunnelMessage Message => this;
}

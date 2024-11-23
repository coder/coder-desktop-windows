using System.Reflection;
using Google.Protobuf;

namespace Coder.Desktop.Rpc.Proto;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class RpcRoleAttribute(string role) : Attribute
{
    public RpcRole Role { get; } = new(role);
}

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

    /// <summary>
    ///     Gets the RpcRole of the message type from it's RpcRole attribute.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="ArgumentException">The message type does not have an RpcRoleAttribute</exception>
    public static RpcRole GetRole()
    {
        var type = typeof(T);
        var attr = type.GetCustomAttribute<RpcRoleAttribute>();
        if (attr is null) throw new ArgumentException($"Message type {type} does not have a RpcRoleAttribute");
        return attr.Role;
    }
}

[RpcRole(RpcRole.Manager)]
public partial class ManagerMessage : RpcMessage<ManagerMessage>
{
    public override RPC RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override ManagerMessage Message => this;
}

[RpcRole(RpcRole.Tunnel)]
public partial class TunnelMessage : RpcMessage<TunnelMessage>
{
    public override RPC RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override TunnelMessage Message => this;
}

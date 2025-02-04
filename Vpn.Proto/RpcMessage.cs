using System.Reflection;
using Google.Protobuf;

namespace Coder.Desktop.Vpn.Proto;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class RpcRoleAttribute : Attribute
{
    public string Role { get; }

    public RpcRoleAttribute(string role)
    {
        Role = role;
    }
}

/// <summary>
///     IRpcMessageCompatibleWith is a marker interface that indicates that a
///     message type can be used to peer with another message type.
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IRpcMessageCompatibleWith<T>;

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
    public abstract RPC? RpcField { get; set; }

    /// <summary>
    ///     The inner message component of the message. This exists so values of type RpcMessage can easily get message
    ///     contents.
    /// </summary>
    public abstract T Message { get; }

    /// <summary>
    ///     Check if the message is valid. Checks for empty <c>oneof</c> of fields.
    /// </summary>
    /// <exception cref="ArgumentException">Invalid message</exception>
    public abstract void Validate();

    /// <summary>
    ///     Gets the RpcRole of the message type from it's RpcRole attribute.
    /// </summary>
    /// <returns>The role string</returns>
    /// <exception cref="ArgumentException">The message type does not have an RpcRoleAttribute</exception>
    public static string GetRole()
    {
        var type = typeof(T);
        var attr = type.GetCustomAttribute<RpcRoleAttribute>();
        if (attr is null) throw new ArgumentException($"Message type '{type}' does not have a RpcRoleAttribute");
        return attr.Role;
    }
}

[RpcRole("manager")]
public partial class ManagerMessage : RpcMessage<ManagerMessage>, IRpcMessageCompatibleWith<TunnelMessage>
{
    public override RPC? RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override ManagerMessage Message => this;

    public override void Validate()
    {
        if (MsgCase == MsgOneofCase.None) throw new ArgumentException("Message does not contain inner message type");
    }
}

[RpcRole("tunnel")]
public partial class TunnelMessage : RpcMessage<TunnelMessage>, IRpcMessageCompatibleWith<ManagerMessage>
{
    public override RPC? RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override TunnelMessage Message => this;

    public override void Validate()
    {
        if (MsgCase == MsgOneofCase.None) throw new ArgumentException("Message does not contain inner message type");
    }
}

[RpcRole("service")]
public partial class ServiceMessage : RpcMessage<ServiceMessage>, IRpcMessageCompatibleWith<ClientMessage>
{
    public override RPC? RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override ServiceMessage Message => this;

    public override void Validate()
    {
        if (MsgCase == MsgOneofCase.None) throw new ArgumentException("Message does not contain inner message type");
    }
}

[RpcRole("client")]
public partial class ClientMessage : RpcMessage<ClientMessage>, IRpcMessageCompatibleWith<ServiceMessage>
{
    public override RPC? RpcField
    {
        get => Rpc;
        set => Rpc = value;
    }

    public override ClientMessage Message => this;

    public override void Validate()
    {
        if (MsgCase == MsgOneofCase.None) throw new ArgumentException("Message does not contain inner message type");
    }
}

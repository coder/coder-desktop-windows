using Coder.Desktop.Rpc.Proto;

namespace Coder.Desktop.Tests.Rpc.Proto;

[TestFixture]
public class RpcRoleAttributeTest
{
    [Test]
    public void Valid()
    {
        var role = new RpcRoleAttribute(RpcRole.Manager);
        Assert.That(role.Role.ToString(), Is.EqualTo(RpcRole.Manager));
        role = new RpcRoleAttribute(RpcRole.Tunnel);
        Assert.That(role.Role.ToString(), Is.EqualTo(RpcRole.Tunnel));
    }

    [Test]
    public void Invalid()
    {
        Assert.Throws<ArgumentException>(() => _ = new RpcRoleAttribute("cats"));
    }
}

[TestFixture]
public class RpcMessageTest
{
    [Test]
    public void GetRole()
    {
        // RpcMessage<RPC> is not a supported message type and doesn't have an
        // RpcRoleAttribute
        var ex = Assert.Throws<ArgumentException>(() => _ = RpcMessage<RPC>.GetRole());
        Assert.That(ex.Message,
            Does.Contain("Message type 'Coder.Desktop.Rpc.Proto.RPC' does not have a RpcRoleAttribute"));

        Assert.That(ManagerMessage.GetRole().ToString(), Is.EqualTo(RpcRole.Manager));
        Assert.That(TunnelMessage.GetRole().ToString(), Is.EqualTo(RpcRole.Tunnel));
    }
}

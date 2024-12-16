using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.Tests.Vpn.Proto;

[TestFixture]
public class RpcRoleAttributeTest
{
    [Test]
    public void Ok()
    {
        var role = new RpcRoleAttribute("manager");
        Assert.That(role.Role, Is.EqualTo("manager"));
        role = new RpcRoleAttribute("tunnel");
        Assert.That(role.Role, Is.EqualTo("tunnel"));
        role = new RpcRoleAttribute("service");
        Assert.That(role.Role, Is.EqualTo("service"));
        role = new RpcRoleAttribute("client");
        Assert.That(role.Role, Is.EqualTo("client"));
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
            Does.Contain("Message type 'Coder.Desktop.Vpn.Proto.RPC' does not have a RpcRoleAttribute"));

        Assert.That(ManagerMessage.GetRole(), Is.EqualTo("manager"));
        Assert.That(TunnelMessage.GetRole(), Is.EqualTo("tunnel"));
        Assert.That(ServiceMessage.GetRole(), Is.EqualTo("service"));
        Assert.That(ClientMessage.GetRole(), Is.EqualTo("client"));
    }
}

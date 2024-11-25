using Coder.Desktop.Rpc.Proto;

namespace Coder.Desktop.Tests.Rpc.Proto;

[TestFixture]
public class RpcRoleTest
{
    [Test(Description = "Instantiate a RpcRole with a valid name")]
    public void ValidRole()
    {
        var role = new RpcRole(RpcRole.Manager);
        Assert.That(role.ToString(), Is.EqualTo(RpcRole.Manager));
        role = new RpcRole(RpcRole.Tunnel);
        Assert.That(role.ToString(), Is.EqualTo(RpcRole.Tunnel));
    }

    [Test(Description = "Try to instantiate a RpcRole with an invalid name")]
    public void InvalidRole()
    {
        Assert.Throws<ArgumentException>(() => _ = new RpcRole("cats"));
    }
}

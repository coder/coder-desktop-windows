using System.Text;
using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.Tests.Vpn.Proto;

[TestFixture]
public class RpcHeaderTest
{
    [Test(Description = "Parse and use some valid header strings")]
    public void Valid()
    {
        var headerStr = "codervpn 2.1 manager";
        var header = RpcHeader.Parse(headerStr);
        Assert.That(header.Role.ToString(), Is.EqualTo(RpcRole.Manager));
        Assert.That(header.Version, Is.EqualTo(new ApiVersion(2, 1)));
        Assert.That(header.ToString(), Is.EqualTo(headerStr + "\n"));
        Assert.That(header.ToBytes().ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(headerStr + "\n")));

        headerStr = "codervpn 1.0 tunnel";
        header = RpcHeader.Parse(headerStr);
        Assert.That(header.Role.ToString(), Is.EqualTo(RpcRole.Tunnel));
        Assert.That(header.Version, Is.EqualTo(new ApiVersion(1, 0)));
        Assert.That(header.ToString(), Is.EqualTo(headerStr + "\n"));
        Assert.That(header.ToBytes().ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(headerStr + "\n")));
    }

    [Test(Description = "Try to parse some invalid header strings")]
    public void ParseInvalid()
    {
        var ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn"));
        Assert.That(ex.Message, Does.Contain("Wrong number of parts"));
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn 1.0 manager cats"));
        Assert.That(ex.Message, Does.Contain("Wrong number of parts"));
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn 1.0"));
        Assert.That(ex.Message, Does.Contain("Wrong number of parts"));
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("cats 1.0 manager"));
        Assert.That(ex.Message, Does.Contain("Invalid preamble"));
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn 1.0 cats"));
        Assert.That(ex.Message, Does.Contain("Unknown role 'cats'"));
    }
}

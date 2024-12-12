using System.Text;
using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.Tests.Vpn.Proto;

[TestFixture]
public class RpcHeaderTest
{
    [Test(Description = "Parse and use some valid header strings")]
    public void Valid()
    {
        var headerStr = "codervpn manager 1.3,2.1";
        var header = RpcHeader.Parse(headerStr);
        Assert.That(header.Role, Is.EqualTo("manager"));
        Assert.That(header.VersionList, Is.EqualTo(new RpcVersionList(new RpcVersion(1, 3), new RpcVersion(2, 1))));
        Assert.That(header.ToString(), Is.EqualTo(headerStr + "\n"));
        Assert.That(header.ToBytes().ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(headerStr + "\n")));

        headerStr = "codervpn tunnel 1.0";
        header = RpcHeader.Parse(headerStr);
        Assert.That(header.Role, Is.EqualTo("tunnel"));
        Assert.That(header.VersionList, Is.EqualTo(new RpcVersionList(new RpcVersion(1, 0))));
        Assert.That(header.ToString(), Is.EqualTo(headerStr + "\n"));
        Assert.That(header.ToBytes().ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(headerStr + "\n")));
    }

    [Test(Description = "Try to parse some invalid header strings")]
    public void ParseInvalid()
    {
        var ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn"));
        Assert.That(ex.Message, Does.Contain("Wrong number of parts"));
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn manager cats 1.0"));
        Assert.That(ex.Message, Does.Contain("Wrong number of parts"));
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn 1.0"));
        Assert.That(ex.Message, Does.Contain("Wrong number of parts"));
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("cats manager 1.0"));
        Assert.That(ex.Message, Does.Contain("Invalid preamble"));
        // RpcHeader doesn't care about the role string as long as it isn't empty.
        ex = Assert.Throws<ArgumentException>(() => RpcHeader.Parse("codervpn  1.0"));
        Assert.That(ex.Message, Does.Contain("Invalid role in header string"));
    }
}

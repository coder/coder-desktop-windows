using Coder.Desktop.Vpn.Proto;
using NUnit.Framework.Constraints;

namespace Coder.Desktop.Tests.Vpn.Proto;

[TestFixture]
public class RpcVersionTest
{
    [Test(Description = "Parse a variety of version strings")]
    public void Parse()
    {
        Assert.That(RpcVersion.Parse("2.1"), Is.EqualTo(new RpcVersion(2, 1)));
        Assert.That(RpcVersion.Parse("1.0"), Is.EqualTo(new RpcVersion(1, 0)));

        Assert.Throws<ArgumentException>(() => RpcVersion.Parse("cats"));
        Assert.Throws<ArgumentException>(() => RpcVersion.Parse("cats.dogs"));
        Assert.Throws<ArgumentException>(() => RpcVersion.Parse("1.dogs"));
        Assert.Throws<ArgumentException>(() => RpcVersion.Parse("1.0.1"));
        Assert.Throws<ArgumentException>(() => RpcVersion.Parse("11"));
    }

    private void IsCompatibleWithBothWays(RpcVersion a, RpcVersion b, IResolveConstraint c)
    {
        Assert.That(a.IsCompatibleWith(b), c);
        Assert.That(b.IsCompatibleWith(a), c);
    }

    [Test(Description = "Test that versions are compatible")]
    public void IsCompatibleWith()
    {
        var twoOne = new RpcVersion(2, 1);
        Assert.That(twoOne.IsCompatibleWith(twoOne), Is.EqualTo(twoOne));

        // 2.1 && 2.2 => 2.1
        IsCompatibleWithBothWays(twoOne, new RpcVersion(2, 2), Is.EqualTo(new RpcVersion(2, 1)));
        // 2.1 && 2.0 => 2.0
        IsCompatibleWithBothWays(twoOne, new RpcVersion(2, 0), Is.EqualTo(new RpcVersion(2, 0)));
        // 2.1 && 3.1 => null
        IsCompatibleWithBothWays(twoOne, new RpcVersion(3, 1), Is.Null);
        // 2.1 && 1.1 => null
        IsCompatibleWithBothWays(twoOne, new RpcVersion(1, 1), Is.Null);
    }
}

[TestFixture]
public class RpcVersionListTest
{
    [Test(Description = "Parse a variety of version list strings")]
    public void Parse()
    {
        Assert.That(RpcVersionList.Parse("1.0"), Is.EqualTo(new RpcVersionList(new RpcVersion(1, 0))));
        Assert.That(RpcVersionList.Parse("1.3,2.1"),
            Is.EqualTo(new RpcVersionList(new RpcVersion(1, 3), new RpcVersion(2, 1))));

        var ex = Assert.Throws<ArgumentException>(() => RpcVersionList.Parse("0.1"));
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException.Message, Does.Contain("Invalid major version"));
        ex = Assert.Throws<ArgumentException>(() => RpcVersionList.Parse(""));
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException.Message, Does.Contain("Invalid version string"));
        ex = Assert.Throws<ArgumentException>(() => RpcVersionList.Parse("2.1,1.1"));
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException.Message, Does.Contain("sorted"));
        ex = Assert.Throws<ArgumentException>(() => RpcVersionList.Parse("1.1,1.2"));
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException.Message, Does.Contain("Duplicate major version"));
    }

    [Test(Description = "Validate a variety of version lists to test every error")]
    public void Validate()
    {
        Assert.DoesNotThrow(() =>
            new RpcVersionList(new RpcVersion(1, 3), new RpcVersion(2, 4), new RpcVersion(3, 0)).Validate());

        var ex = Assert.Throws<ArgumentException>(() => new RpcVersionList(new RpcVersion(0, 1)).Validate());
        Assert.That(ex.Message, Does.Contain("Invalid major version"));
        ex = Assert.Throws<ArgumentException>(() =>
            new RpcVersionList(new RpcVersion(2, 1), new RpcVersion(1, 2)).Validate());
        Assert.That(ex.Message, Does.Contain("sorted"));
        ex = Assert.Throws<ArgumentException>(() =>
            new RpcVersionList(new RpcVersion(1, 1), new RpcVersion(1, 2)).Validate());
        Assert.That(ex.Message, Does.Contain("Duplicate major version"));
    }

    private void IsCompatibleWithBothWays(RpcVersionList a, RpcVersionList b, IResolveConstraint c)
    {
        Assert.That(a.IsCompatibleWith(b), c);
        Assert.That(b.IsCompatibleWith(a), c);
    }

    [Test(Description = "Check a variety of lists against each other to determine compatible version")]
    public void IsCompatibleWith()
    {
        var list1 = RpcVersionList.Parse("1.2,2.4,3.2");
        Assert.That(list1.IsCompatibleWith(list1), Is.EqualTo(new RpcVersion(3, 2)));

        IsCompatibleWithBothWays(list1, RpcVersionList.Parse("4.1,5.2"), Is.Null);
        IsCompatibleWithBothWays(list1, RpcVersionList.Parse("1.2,2.3"), Is.EqualTo(new RpcVersion(2, 3)));
        IsCompatibleWithBothWays(list1, RpcVersionList.Parse("2.3,3.3"), Is.EqualTo(new RpcVersion(3, 2)));
    }
}

using Coder.Desktop.Rpc.Proto;

namespace Coder.Desktop.Tests.Rpc.Proto;

[TestFixture]
public class ApiVersionTest
{
    [Test(Description = "Parse a variety of version strings")]
    public void Parse()
    {
        Assert.That(ApiVersion.Parse("2.1"), Is.EqualTo(new ApiVersion(2, 1)));
        Assert.That(ApiVersion.Parse("1.0"), Is.EqualTo(new ApiVersion(1, 0)));

        Assert.Throws<ArgumentException>(() => ApiVersion.Parse("cats"));
        Assert.Throws<ArgumentException>(() => ApiVersion.Parse("cats.dogs"));
        Assert.Throws<ArgumentException>(() => ApiVersion.Parse("1.dogs"));
        Assert.Throws<ArgumentException>(() => ApiVersion.Parse("1.0.1"));
        Assert.Throws<ArgumentException>(() => ApiVersion.Parse("11"));
    }

    [Test(Description = "Test that versions are compatible")]
    public void Validate()
    {
        var twoOne = new ApiVersion(2, 1, 1);
        Assert.DoesNotThrow(() => twoOne.Validate(twoOne));
        Assert.DoesNotThrow(() => twoOne.Validate(new ApiVersion(2, 0)));
        Assert.DoesNotThrow(() => twoOne.Validate(new ApiVersion(1, 0)));

        var ex = Assert.Throws<ApiCompatibilityException>(() => twoOne.Validate(new ApiVersion(2, 2)));
        Assert.That(ex.Message, Does.Contain("Peer supports newer minor version"));
        ex = Assert.Throws<ApiCompatibilityException>(() => twoOne.Validate(new ApiVersion(3, 1)));
        Assert.That(ex.Message, Does.Contain("Peer supports newer major version"));
        ex = Assert.Throws<ApiCompatibilityException>(() => twoOne.Validate(new ApiVersion(0, 8)));
        Assert.That(ex.Message, Does.Contain("Version is no longer supported"));
    }
}

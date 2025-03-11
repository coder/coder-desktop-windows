using Coder.Desktop.Vpn.Utilities;
using Semver;

namespace Coder.Desktop.Tests.Vpn.Utilities;

[TestFixture]
public class ServerVersionUtilitiesTest
{
    [Test(Description = "Test invalid versions")]
    public void InvalidVersions()
    {
        var invalidVersions = new List<(string, string)>
        {
            (null!, "Server version is empty"),
            ("", "Server version is empty"),
            (" ", "Server version is empty"),
            ("v", "Could not parse server version"),
            ("1", "Could not parse server version"),
            ("v1", "Could not parse server version"),
            ("1.2", "Could not parse server version"),
            ("v1.2", "Could not parse server version"),
            ("1.2.3.4", "Could not parse server version"),
            ("v1.2.3.4", "Could not parse server version"),

            ("1.2.3", "not within required server version range"),
            ("v1.2.3", "not within required server version range"),
            ("2.19.0-devel", "not within required server version range"),
            ("v2.19.0-devel", "not within required server version range"),
        };

        foreach (var (version, expectedErrorMessage) in invalidVersions)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                ServerVersionUtilities.ParseAndValidateServerVersion(version));
            Assert.That(ex.Message, Does.Contain(expectedErrorMessage));
        }
    }

    [Test(Description = "Test valid versions")]
    public void ValidVersions()
    {
        var validVersions = new List<ServerVersion>
        {
            new()
            {
                RawString = "2.20.0-devel+17f8e93d0",
                SemVersion = new SemVersion(2, 20, 0, ["devel"], ["17f8e93d0"]),
            },
            new()
            {
                RawString = "2.20.0",
                SemVersion = new SemVersion(2, 20, 0),
            },
            new()
            {
                RawString = "2.21.3",
                SemVersion = new SemVersion(2, 21, 3),
            },
            new()
            {
                RawString = "3.0.0",
                SemVersion = new SemVersion(3, 0, 0),
            },
        };

        foreach (var version in validVersions)
            foreach (var prefix in new[] { "", "v" })
            {
                var result = ServerVersionUtilities.ParseAndValidateServerVersion(prefix + version.RawString);
                Assert.That(result.RawString, Is.EqualTo(prefix + version.RawString), version.RawString);
                Assert.That(result.SemVersion, Is.EqualTo(version.SemVersion), version.RawString);
            }
    }
}

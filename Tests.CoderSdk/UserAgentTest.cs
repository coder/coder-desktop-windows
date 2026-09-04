using System.Reflection;
using System.Text.RegularExpressions;
using Coder.Desktop.CoderSdk;

namespace Coder.Desktop.Tests.CoderSdk;

[TestFixture]
public class UserAgentTest
{
    // Every Coder client is expected to match this, so operators can allowlist the token set. Keep it in
    // sync with the Coder CLI (cli/root.go) and the macOS and Linux Desktop clients.
    private static readonly Regex Grammar = new(
        @"^coder-(cli|desktop|desktop-core|vpn-daemon)/\d+\.\d+\.\d+ \((windows|darwin|linux|unknown)/(386|amd64|arm|arm64|unknown)(; .+)?\)$");

    [Test(Description = "Matches the shared User-Agent grammar")]
    [TestCase(CoderComponent.Desktop)]
    [TestCase(CoderComponent.Core)]
    public void MatchesGrammar(CoderComponent component)
    {
        Assert.That(UserAgent.Build(component), Does.Match(Grammar));
    }

    [Test(Description = "Each component reports its own token")]
    public void ComponentTokens()
    {
        Assert.That(UserAgent.Build(CoderComponent.Desktop), Does.StartWith("coder-desktop/"));
        Assert.That(UserAgent.Build(CoderComponent.Core), Does.StartWith("coder-desktop-core/"));
    }

    [Test(Description = "Four-part assembly versions are trimmed to the three-part release version")]
    public void TrimsAssemblyVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        Assert.That(version, Is.Not.Null, "test assembly has no version");

        var ua = UserAgent.Build(CoderComponent.Desktop, assembly);
        Assert.That(ua, Does.Match(Grammar));
        Assert.That(ua, Does.StartWith($"coder-desktop/{version!.Major}.{version.Minor}.{version.Build} ("));
    }

    [Test(Description = "An unversioned assembly still produces a well-formed User-Agent")]
    public void UnknownVersion()
    {
        var ua = UserAgent.Build(CoderComponent.Desktop, null);
        Assert.That(ua, Does.Match(Grammar));
        Assert.That(ua, Does.StartWith("coder-desktop/0.0.0 ("));
    }

    [Test(Description = "Unrecognized components are rejected rather than silently mislabeled")]
    public void UnknownComponent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UserAgent.Build((CoderComponent)(-1)));
    }
}

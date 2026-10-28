using Coder.Desktop.App.Utils;

namespace Coder.Desktop.Tests.App.Utils;

[TestFixture]
public class SessionTokenLinkTest
{
    private static readonly Uri Deployment = new("https://coder.example.com/");

    // Builds a link with `url` set to `origin` and the placeholder as `token`.
    private static string Link(string prefix, string origin = "https://coder.example.com", string extra = "")
    {
        return $"{prefix}url={origin}&token={SessionTokenLink.Placeholder}{extra}";
    }

    [TestCase("vscode://coder.coder-remote/open?owner=a&")]
    [TestCase("cursor://coder.coder-remote/openDevContainer?")]
    [TestCase("VSCode://Coder.Coder-Remote/open?")]
    [TestCase("jetbrains://gateway/coder?")]
    [TestCase("jetbrains://gateway/com.coder.toolbox?")]
    [TestCase("jetbrains-gateway://connect#type=coder&")]
    public void TrustedHandler(string prefix)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(Link(prefix), Deployment), Is.True);
    }

    [TestCase("https://coder.example.com/")]
    [TestCase("https://CODER.example.com")]
    [TestCase("https://coder.example.com:443")]
    [TestCase("https%3A%2F%2Fcoder.example.com")]
    public void TrustedOrigin(string origin)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(Link("vscode://coder.coder-remote/open?", origin), Deployment),
            Is.True);
    }

    [TestCase("vscode://other.ext/open?")]
    [TestCase("coder://open?")]
    [TestCase("vscode:coder.coder-remote/open?")]
    [TestCase("vscode://x@coder.coder-remote/open?")]
    [TestCase("vscode://coder.coder-remote/open/../open?")]
    [TestCase("vscode://coder.coder-remote/open#")]
    [TestCase("jetbrains-gateway://connect?")]
    [TestCase("jetbrains-gateway://connect#type=ssh&")]
    [TestCase("\u212Airo://coder.coder-remote/open?")]
    public void UntrustedHandler(string prefix)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(Link(prefix), Deployment), Is.False);
    }

    [TestCase("https://other.example")]
    [TestCase("https://coder.example.com:1")]
    [TestCase("http://coder.example.com")]
    [TestCase("https%3A%2F%2Fother.example")]
    [TestCase("not-a-url")]
    [TestCase("https://coder.example.com/@victim/ws.main/apps/x/")]
    [TestCase("https://user@coder.example.com")]
    [TestCase("https://other.example%252Fx@coder.example.com/")]
    [TestCase("https://coder.example.com/.")]
    [TestCase("https://coder.example.com?")]
    [TestCase("https://coder.example.com%0A")]
    public void UntrustedOrigin(string origin)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(Link("vscode://coder.coder-remote/open?", origin), Deployment),
            Is.False);
    }

    [TestCase("&url=https://other.example")]
    [TestCase("&t=$SESSION_TOKEN")]
    [TestCase("#url=https://other.example")]
    [TestCase("&type=ssh")]
    [TestCase("%FF")]
    [TestCase("&x=%26url%3Dhttps%253A%252F%252Fother.example")]
    [TestCase("&x=%252526url%25253Dhttps%25253A%25252F%25252Fother.example")]
    [TestCase("&x=%26url=%CC%81https://other.example")]
    [TestCase("&ur\tl=https://other.example")]
    [TestCase("&%75rl=https://other.example")]
    public void UntrustedExtraParams(string extra)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(
            Link("jetbrains-gateway://connect#type=coder&", extra: extra), Deployment), Is.False);
    }

    [TestCase("vscode://coder.coder-remote/open?token=$SESSION_TOKEN")]
    [TestCase("vscode://coder.coder-remote/open?url=https://coder.example.com#token=$SESSION_TOKEN")]
    [TestCase("vscode://coder.coder-remote/open?x=1#&url=https://coder.example.com&token=$SESSION_TOKEN")]
    [TestCase("jetbrains://gateway/coder?url=https://coder.example.com&token=x&ide_download_link=https://other.example/$SESSION_TOKEN")]
    [TestCase("jetbrains-gateway://connect#type=coder&url=https://coder.example.com&token=%24SESSION_TOKEN&x=$SESSION_TOKEN")]
    public void UntrustedTokenPlacement(string url)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(url, Deployment), Is.False);
    }

    [Test]
    public void Substitute()
    {
        var url = Link("vscode://coder.coder-remote/open?");
        Assert.That(SessionTokenLink.Substitute(url, Deployment, "secret", false), Does.EndWith("&token=secret"));
        Assert.That(SessionTokenLink.Substitute(url, Deployment, "secret", true), Does.EndWith("&token="));
        Assert.That(SessionTokenLink.Substitute(url, null, "secret", false), Is.Null);
        Assert.That(SessionTokenLink.Substitute(Link("vscode://other.ext/open?"), Deployment, "secret", false), Is.Null);
    }

    [Test]
    public void IsBlocked()
    {
        Assert.That(SessionTokenLink.IsBlocked(Link("vscode://other.ext/open?"), Deployment), Is.True);
        Assert.That(SessionTokenLink.IsBlocked("someapp://anything?x=1", Deployment), Is.False);
    }

    [TestCase("vscode://coder.coder-remote/open?owner=a", true)]
    [TestCase("jetbrains-gateway://connect#type=coder&workspace=b", true)]
    [TestCase("jetbrains-gateway://connect#type=ssh&host=other.example", false)]
    [TestCase("someapp://anything?x=1", false)]
    public void IsKnownHandler(string url, bool expected)
    {
        Assert.That(SessionTokenLink.IsKnownHandler(url), Is.EqualTo(expected));
    }
}

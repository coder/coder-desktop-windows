using Coder.Desktop.App.Utils;

namespace Coder.Desktop.Tests.App.Utils;

[TestFixture]
public class SessionTokenLinkTest
{
    private static readonly Uri SignedIn = new("https://coder.example.com/");
    private static readonly Uri Dashboard = new("https://dashboard.example/");
    private const string Vscode = "vscode://coder.coder-remote/open?";
    private const string Gateway = "jetbrains-gateway://connect#type=coder&";

    // Builds a link with `url` set to `origin` and the placeholder as `token`.
    private static string Link(string prefix, string origin = "https://coder.example.com", string extra = "")
    {
        return $"{prefix}url={origin}&token={SessionTokenLink.Placeholder}{extra}";
    }

    private static TestCaseData Trusted(string name, string url)
    {
        return new TestCaseData(url).Returns(null).SetName(name);
    }

    private static TestCaseData Untrusted(string name, string url)
    {
        return new TestCaseData(url).Returns(SessionTokenLink.UntrustedUrlMessage).SetName(name);
    }

    private static TestCaseData WrongDeployment(string name, string origin, string? shown = null)
    {
        return new TestCaseData(Link(Vscode, origin))
            .Returns($"This app signs in to {shown ?? origin}, but Coder Desktop is signed in to https://coder.example.com.")
            .SetName(name);
    }

    private static readonly TestCaseData[] Cases =
    [
        Trusted("no placeholder", "someapp://anything?x=1"),
        Trusted("VS Code", Link("vscode://coder.coder-remote/open?owner=a&")),
        Trusted("dev container handler", Link("cursor://coder.coder-remote/openDevContainer?")),
        Trusted("uppercase scheme and authority", Link("VSCode://Coder.Coder-Remote/open?")),
        Trusted("Gateway plugin", Link("jetbrains://gateway/coder?")),
        Trusted("Toolbox", Link("jetbrains://gateway/com.coder.toolbox?")),
        Trusted("Gateway fragment", Link(Gateway)),
        Trusted("trailing slash", Link(Vscode, "https://coder.example.com/")),
        Trusted("uppercase host", Link(Vscode, "https://CODER.example.com")),
        Trusted("default port", Link(Vscode, "https://coder.example.com:443")),
        Trusted("encoded url value", Link(Vscode, "https%3A%2F%2Fcoder.example.com")),
        Trusted("dashboard URL", Link(Vscode, "https://dashboard.example")),

        Untrusted("unknown extension", Link("vscode://other.ext/open?")),
        Untrusted("unknown scheme", Link("coder://open?")),
        Untrusted("opaque handler", Link("vscode:coder.coder-remote/open?")),
        Untrusted("userinfo in handler", Link("vscode://x@coder.coder-remote/open?")),
        Untrusted("dot segments in handler", Link("vscode://coder.coder-remote/open/../open?")),
        Untrusted("VS Code params in fragment", Link("vscode://coder.coder-remote/open#")),
        Untrusted("Gateway params in query", Link("jetbrains-gateway://connect?")),
        Untrusted("non-Coder Gateway provider", Link("jetbrains-gateway://connect#type=ssh&")),
        Untrusted("Kelvin sign in scheme", Link("\u212Airo://coder.coder-remote/open?")),
        Untrusted("tab in a param name", Link(Gateway, extra: "&ur\tl=https://other.example")),
        Untrusted("second placeholder", Link(Gateway, extra: "&x=$SESSION_TOKEN")),
        Untrusted("params after a fragment", Link($"{Vscode}x=1#&")),
        Untrusted("placeholder outside the raw token entry",
            $"{Vscode}url=https://coder.example.com&token=%24SESSION_TOKEN&y=$SESSION_TOKEN"),
        Untrusted("encoded url name", $"{Vscode}%75rl=https://coder.example.com&token=$SESSION_TOKEN"),
        Untrusted("double-encoded url param", Link(Gateway, extra: "&x=%2526url%253Dhttps://other.example")),
        Untrusted("url smuggled in an encoded value", Link(Gateway, extra: "&x=a%26url%3Dhttps://other.example")),
        Untrusted("duplicate url", Link(Gateway, extra: "&url=https://other.example")),
        Untrusted("duplicate Gateway type", Link(Gateway, extra: "&type=ssh")),
        Untrusted("no url", $"{Vscode}token=$SESSION_TOKEN"),
        Untrusted("url isn't a URL", Link(Vscode, "not-a-url")),
        Untrusted("url has a path", Link(Vscode, "https://coder.example.com/@victim/ws.main/apps/x/")),
        Untrusted("url has userinfo", Link(Vscode, "https://user@coder.example.com")),
        Untrusted("url has a dot segment", Link(Vscode, "https://coder.example.com/.")),
        Untrusted("url has an empty query", Link(Vscode, "https://coder.example.com?")),

        WrongDeployment("another host", "https://other.example"),
        WrongDeployment("another port", "https://coder.example.com:1"),
        WrongDeployment("http instead of https", "http://coder.example.com"),
        WrongDeployment("another host with a trailing slash", "https://other.example/", "https://other.example"),
    ];

    [TestCaseSource(nameof(Cases))]
    public string? BlockReason(string url)
    {
        return SessionTokenLink.BlockReason(url, SignedIn, Dashboard);
    }

    [TestCase("vscode://coder.coder-remote/open?owner=a", ExpectedResult = true)]
    [TestCase("jetbrains-gateway://connect#type=coder&workspace=b", ExpectedResult = true)]
    [TestCase("jetbrains-gateway://connect#type=ssh&host=other.example", ExpectedResult = false)]
    [TestCase("someapp://anything?x=1", ExpectedResult = false)]
    public bool IsKnownHandler(string url)
    {
        return SessionTokenLink.IsKnownHandler(url);
    }
}

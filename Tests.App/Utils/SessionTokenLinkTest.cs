using Coder.Desktop.App.Utils;

namespace Coder.Desktop.Tests.App.Utils;

[TestFixture]
public class SessionTokenLinkTest
{
    private const string O = "https://coder.example.com";
    private const string T = SessionTokenLink.Placeholder;
    private static readonly Uri Deployment = new(O + "/");

    [Test(Description = "Allows links to known handlers for this deployment")]
    [TestCase("vscode://coder.coder-remote/open?owner=a&workspace=b&url=" + O + "&token=" + T)]
    [TestCase("cursor://coder.coder-remote/open?url=" + O + "/&token=" + T)]
    [TestCase("jetbrains://gateway/coder?&workspace=b&url=" + O + "&token=" + T + "&ide_product_code=GO")]
    [TestCase("jetbrains-gateway://connect#type=coder&workspace=b&url=" + O + "&token=" + T)]
    [TestCase("cursor://coder.coder-remote/openDevContainer?url=" + O + "&token=" + T)]
    [TestCase("jetbrains://gateway/com.coder.toolbox?url=" + O + "&token=" + T)]
    [TestCase("VSCode://Coder.Coder-Remote/open?url=" + O + "&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=https://CODER.example.com&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=https://coder.example.com:443&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=https%3A%2F%2Fcoder.example.com&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?folder=/home/a%20b&url=" + O + "&token=" + T)]
    public void Trusted(string url)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(url, Deployment), Is.True);
        Assert.That(SessionTokenLink.IsBlocked(url, Deployment), Is.False);
    }

    [Test(Description = "Rejects links that could send the token elsewhere")]
    [TestCase("vscode://coder.coder-remote/open?url=https://other.example&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=https://coder.example.com:1&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=http://coder.example.com&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=https%3A%2F%2Fother.example&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=not-a-url&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "&url=https://other.example&token=" + T)]
    [TestCase("jetbrains-gateway://connect?url=https://other.example#url=" + O + "&token=" + T)]
    [TestCase("jetbrains://gateway/coder?url=" + O + "&token=x&ide_download_link=https://other.example/" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "&token=" + T + "&t=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "#token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "&token=" + T + "#url=https://other.example")]
    [TestCase("vscode://other.ext/open?url=" + O + "&token=" + T)]
    [TestCase("coder://open?url=" + O + "&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "/@victim/ws.main/apps/x/&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=https://other.example%252Fx@coder.example.com/&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?owner=x%26url%3Dhttps%253A%252F%252Fother.example&url=" + O +
              "&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?x=%252526url%25253Dhttps%25253A%25252F%25252Fother.example&url=" +
              O + "&token=" + T)]
    [TestCase("vscode:coder.coder-remote/open?url=" + O + "&token=" + T)]
    [TestCase("vscode://x@coder.coder-remote/open?url=" + O + "&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open/../open?url=" + O + "&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open#url=" + O + "&token=" + T)]
    [TestCase("jetbrains-gateway://connect?url=" + O + "&token=" + T)]
    [TestCase("jetbrains-gateway://connect#type=coder&url=" + O + "&token=%24SESSION_TOKEN&ide_download_link=https://other.example/" + T)]
    [TestCase("jetbrains-gateway://connect#type=coder&%75rl=" + O + "&token=" + T)]
    [TestCase("jetbrains-gateway://connect#type=ssh&host=other.example&url=" + O + "&token=" + T)]
    [TestCase("jetbrains-gateway://connect#type=coder&url=" + O + "&token=" + T + "&type=ssh")]
    [TestCase("vscode://coder.coder-remote/open?x=1#&url=" + O + "&token=" + T)]
    [TestCase("\u212Airo://coder.coder-remote/open?url=" + O + "&token=" + T)]
    [TestCase("jetbrains-gateway://connect#type=coder&url=" + O + "&ur\tl=https://other.example&token=" + T)]
    [TestCase("jetbrains-gateway://connect#type=coder&url=" + O + "&ur\nl=https://other.example&token=" + T)]
    [TestCase("jetbrains-gateway://connect#type=coder&url=" + O + "&ur\rl=https://other.example&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "/.&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "?&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "%0A&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "&token=" + T + "%FF")]
    [TestCase("vscode://coder.coder-remote/open?x=%25%CC%81&url=" + O + "&token=" + T)]
    [TestCase("vscode://coder.coder-remote/open?url=" + O + "&token=" + T + "&x=%26url=%CC%81https://other.example")]
    public void Untrusted(string url)
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(url, Deployment), Is.False);
        Assert.That(SessionTokenLink.IsBlocked(url, Deployment), Is.True);
        Assert.That(SessionTokenLink.Substitute(url, Deployment, "secret", false), Is.Null);
    }

    [Test(Description = "Rejects every link when the deployment is unknown")]
    public void NoDeployment()
    {
        Assert.That(SessionTokenLink.IsDestinationTrusted(
            "vscode://coder.coder-remote/open?url=" + O + "&token=" + T, null), Is.False);
    }

    [Test(Description = "Substitutes the token into trusted links")]
    public void Substitute()
    {
        const string url = "vscode://coder.coder-remote/open?url=" + O + "&token=" + T;
        Assert.That(SessionTokenLink.Substitute(url, Deployment, "secret", false),
            Is.EqualTo("vscode://coder.coder-remote/open?url=" + O + "&token=secret"));
    }

    [Test(Description = "Sub-agent apps never get a token")]
    public void SubstituteSubAgent()
    {
        const string url = "vscode://coder.coder-remote/openDevContainer?url=" + O + "&token=" + T;
        Assert.That(SessionTokenLink.Substitute(url, Deployment, "secret", true),
            Is.EqualTo("vscode://coder.coder-remote/openDevContainer?url=" + O + "&token="));
    }

    [Test(Description = "Known Coder IDE handlers")]
    [TestCase("vscode://coder.coder-remote/open?owner=a&workspace=b", true)]
    [TestCase("JetBrains://Gateway/coder?workspace=b", true)]
    [TestCase("jetbrains-gateway://connect#type=coder&workspace=b", true)]
    [TestCase("jetbrains-gateway://connect#type=ssh&host=other.example", false)]
    [TestCase("vscode://other.ext/open?x=1", false)]
    [TestCase("someapp://anything?x=1", false)]
    public void KnownHandler(string url, bool expected)
    {
        Assert.That(SessionTokenLink.IsKnownHandler(url), Is.EqualTo(expected));
    }

    [Test(Description = "Links without the placeholder aren't blocked")]
    public void NoPlaceholder()
    {
        Assert.That(SessionTokenLink.IsBlocked("someapp://anything?x=1", Deployment), Is.False);
    }

    [Test(Description = "Bare origins")]
    [TestCase("https://coder.example.com", true)]
    [TestCase("https://coder.example.com/", true)]
    [TestCase("http://localhost:3000", true)]
    [TestCase("https://coder.example.com/x", false)]
    [TestCase("https://user@coder.example.com", false)]
    [TestCase("https://coder.example.com?", false)]
    [TestCase("https://coder.example.com#", false)]
    [TestCase("https://coder.example.com/.", false)]
    [TestCase("https://coder.example.com\n", false)]
    [TestCase("https://coder example.com", false)]
    [TestCase("coder.example.com", false)]
    public void BareOrigin(string url, bool expected)
    {
        Assert.That(SessionTokenLink.IsBareOrigin(url), Is.EqualTo(expected));
    }
}

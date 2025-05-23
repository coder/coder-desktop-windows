using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.Vpn.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog;

namespace Coder.Desktop.Tests.App.Services;

[TestFixture]
public class UriHandlerTest
{
    [SetUp]
    public void SetupMocksAndUriHandler()
    {
        Serilog.Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.NUnitOutput().CreateLogger();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSerilog();
        var logger = (ILogger<UriHandler>)builder.Build().Services.GetService(typeof(ILogger<UriHandler>))!;

        _mUserNotifier = new Mock<IUserNotifier>(MockBehavior.Strict);
        _mRdpConnector = new Mock<IRdpConnector>(MockBehavior.Strict);
        _mRpcController = new Mock<IRpcController>(MockBehavior.Strict);
        _mCredentialManager = new Mock<ICredentialManager>(MockBehavior.Strict);

        uriHandler = new UriHandler(logger,
            _mRpcController.Object,
            _mUserNotifier.Object,
            _mRdpConnector.Object,
            _mCredentialManager.Object);
    }

    private Mock<IUserNotifier> _mUserNotifier;
    private Mock<IRdpConnector> _mRdpConnector;
    private Mock<IRpcController> _mRpcController;
    private Mock<ICredentialManager> _mCredentialManager;
    private UriHandler uriHandler; // Unit under test.

    [SetUp]
    public void AgentWorkspaceAndCredentialFixtures()
    {
        agent11 = new Agent();
        agent11.Fqdn.Add("workspace1.coder");
        agent11.Id = ByteString.CopyFrom(0x1, 0x1);
        agent11.WorkspaceId = ByteString.CopyFrom(0x1, 0x0);
        agent11.Name = "agent11";

        workspace1 = new Workspace
        {
            Id = ByteString.CopyFrom(0x1, 0x0),
            Name = "workspace1",
            Status = Workspace.Types.Status.Running,
        };

        modelWithWorkspace1 = new RpcModel
        {
            VpnLifecycle = VpnLifecycle.Started,
            Workspaces = [workspace1],
            Agents = [agent11],
        };

        credentialModel1 = new CredentialModel
        {
            State = CredentialState.Valid,
            CoderUrl = new Uri("https://coder.test"),
        };
    }

    private Agent agent11;
    private Workspace workspace1;
    private RpcModel modelWithWorkspace1;
    private CredentialModel credentialModel1;

    [Test(Description = "Open RDP with username & password")]
    [CancelAfter(30_000)]
    public async Task Mainline(CancellationToken ct)
    {
        var input = new Uri(
            "coder://coder.test/v0/open/ws/workspace1/agent/agent11/rdp?username=testy&password=sesame");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        var expectedCred = new RdpCredentials("testy", "sesame");
        _ = _mRdpConnector.Setup(m => m.WriteCredentials(agent11.Fqdn[0], expectedCred));
        _ = _mRdpConnector.Setup(m => m.Connect(agent11.Fqdn[0], IRdpConnector.DefaultPort, ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mRdpConnector.Verify(m => m.WriteCredentials(It.IsAny<string>(), It.IsAny<RdpCredentials>()));
        _mRdpConnector.Verify(m => m.Connect(It.IsAny<string>(), It.IsAny<int>(), ct), Times.Once);
    }

    [Test(Description = "Open RDP with no credentials")]
    [CancelAfter(30_000)]
    public async Task NoCredentials(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v0/open/ws/workspace1/agent/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _ = _mRdpConnector.Setup(m => m.Connect(agent11.Fqdn[0], IRdpConnector.DefaultPort, ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mRdpConnector.Verify(m => m.Connect(It.IsAny<string>(), It.IsAny<int>(), ct), Times.Once);
    }

    [Test(Description = "Unknown app slug")]
    [CancelAfter(30_000)]
    public async Task UnknownApp(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v0/open/ws/workspace1/agent/agent11/someapp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("someapp"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Unknown agent name")]
    [CancelAfter(30_000)]
    public async Task UnknownAgent(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v0/open/ws/workspace1/agent/wrongagent/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("wrongagent"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Unknown workspace name")]
    [CancelAfter(30_000)]
    public async Task UnknownWorkspace(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v0/open/ws/wrongworkspace/agent/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("wrongworkspace"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Malformed Query String")]
    [CancelAfter(30_000)]
    public async Task MalformedQuery(CancellationToken ct)
    {
        // there might be some query string that gets the parser to throw an exception, but I could not find one.
        var input = new Uri("coder://coder.test/v0/open/ws/workspace1/agent/agent11/rdp?%&##");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        // treated the same as if we just didn't include credentials
        _ = _mRdpConnector.Setup(m => m.Connect(agent11.Fqdn[0], IRdpConnector.DefaultPort, ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mRdpConnector.Verify(m => m.Connect(It.IsAny<string>(), It.IsAny<int>(), ct), Times.Once);
    }

    [Test(Description = "VPN not started")]
    [CancelAfter(30_000)]
    public async Task VPNNotStarted(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v0/open/ws/wrongworkspace/agent/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(new RpcModel
        {
            VpnLifecycle = VpnLifecycle.Starting,
        });
        // Coder Connect is the user facing name, so make sure the error mentions it.
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("Coder Connect"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Wrong number of components")]
    [CancelAfter(30_000)]
    public async Task UnknownNumComponents(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v0/open/ws/wrongworkspace/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Unknown prefix")]
    [CancelAfter(30_000)]
    public async Task UnknownPrefix(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v300/open/ws/workspace1/agent/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Unknown authority")]
    [CancelAfter(30_000)]
    public async Task UnknownAuthority(CancellationToken ct)
    {
        var input = new Uri("coder://unknown.test/v0/open/ws/workspace1/agent/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex(@"unknown\.test"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Missing authority")]
    [CancelAfter(30_000)]
    public async Task MissingAuthority(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/workspace1/agent/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(credentialModel1);
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("Coder server"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }

    [Test(Description = "Not signed in")]
    [CancelAfter(30_000)]
    public async Task NotSignedIn(CancellationToken ct)
    {
        var input = new Uri("coder://coder.test/v0/open/ws/workspace1/agent/agent11/rdp");

        _mCredentialManager.Setup(m => m.GetCachedCredentials()).Returns(new CredentialModel()
        {
            State = CredentialState.Invalid,
        });
        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("signed in"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
        _mUserNotifier.Verify(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct), Times.Once());
    }
}

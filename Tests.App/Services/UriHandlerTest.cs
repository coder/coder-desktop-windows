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

        uriHandler = new UriHandler(logger, _mRpcController.Object, _mUserNotifier.Object, _mRdpConnector.Object);
    }

    [TearDown]
    public async Task CleanupUriHandler()
    {
        await uriHandler.DisposeAsync();
    }

    private Mock<IUserNotifier> _mUserNotifier;
    private Mock<IRdpConnector> _mRdpConnector;
    private Mock<IRpcController> _mRpcController;
    private UriHandler uriHandler; // Unit under test.

    [SetUp]
    public void AgentAndWorkspaceFixtures()
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
        };

        modelWithWorkspace1 = new RpcModel
        {
            VpnLifecycle = VpnLifecycle.Started,
            Workspaces = [workspace1],
            Agents = [agent11],
        };
    }

    private Agent agent11;
    private Workspace workspace1;
    private RpcModel modelWithWorkspace1;

    [Test(Description = "Open RDP with username & password")]
    [CancelAfter(30_000)]
    public async Task Mainline(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/workspace1/agent/agent11/rdp?username=testy&password=sesame");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        var expectedCred = new RdpCredentials("testy", "sesame");
        _ = _mRdpConnector.Setup(m => m.WriteCredentials(agent11.Fqdn[0], expectedCred, ct))
            .Returns(Task.CompletedTask);
        _ = _mRdpConnector.Setup(m => m.Connect(agent11.Fqdn[0], IRdpConnector.DefaultPort, ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "Open RDP with no credentials")]
    [CancelAfter(30_000)]
    public async Task NoCredentials(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/workspace1/agent/agent11/rdp");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _ = _mRdpConnector.Setup(m => m.Connect(agent11.Fqdn[0], IRdpConnector.DefaultPort, ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "Unknown app slug")]
    [CancelAfter(30_000)]
    public async Task UnknownApp(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/workspace1/agent/agent11/someapp");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("someapp"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "Unknown agent name")]
    [CancelAfter(30_000)]
    public async Task UnknownAgent(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/workspace1/agent/wrongagent/rdp");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("wrongagent"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "Unknown workspace name")]
    [CancelAfter(30_000)]
    public async Task UnknownWorkspace(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/wrongworkspace/agent/agent11/rdp");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("wrongworkspace"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "Malformed Query String")]
    [CancelAfter(30_000)]
    public async Task MalformedQuery(CancellationToken ct)
    {
        // there might be some query string that gets the parser to throw an exception, but I could not find one.
        var input = new Uri("coder:/v0/open/ws/workspace1/agent/agent11/rdp?%&##");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        // treated the same as if we just didn't include credentials
        _ = _mRdpConnector.Setup(m => m.Connect(agent11.Fqdn[0], IRdpConnector.DefaultPort, ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "VPN not started")]
    [CancelAfter(30_000)]
    public async Task VPNNotStarted(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/wrongworkspace/agent/agent11/rdp");

        _mRpcController.Setup(m => m.GetState()).Returns(new RpcModel
        {
            VpnLifecycle = VpnLifecycle.Starting,
        });
        // Coder Connect is the user facing name, so make sure the error mentions it.
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsRegex("Coder Connect"), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "Wrong number of components")]
    [CancelAfter(30_000)]
    public async Task UnknownNumComponents(CancellationToken ct)
    {
        var input = new Uri("coder:/v0/open/ws/wrongworkspace/agent11/rdp");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }

    [Test(Description = "Unknown prefix")]
    [CancelAfter(30_000)]
    public async Task UnknownPrefix(CancellationToken ct)
    {
        var input = new Uri("coder:/v300/open/ws/workspace1/agent/agent11/rdp");

        _mRpcController.Setup(m => m.GetState()).Returns(modelWithWorkspace1);
        _mUserNotifier.Setup(m => m.ShowErrorNotification(It.IsAny<string>(), It.IsAny<string>(), ct))
            .Returns(Task.CompletedTask);
        await uriHandler.HandleUri(input, ct);
    }
}

using System.Reflection;
using Coder.Desktop.Vpn;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Coder.Desktop.Tests.Vpn.Service;

internal class FakeTunnelSupervisor(RpcVersion? negotiatedVersion, Exception? sendException = null)
    : ITunnelSupervisor
{
    public List<ManagerMessage> SentRequests { get; } = [];
    public TaskCompletionSource Sent { get; } = new();
    public int StopCalls { get; private set; }

    public RpcVersion? NegotiatedVersion => negotiatedVersion;

    public ValueTask<TunnelMessage> SendRequestAwaitReply(ManagerMessage message, CancellationToken ct = default)
    {
        SentRequests.Add(message);
        Sent.TrySetResult();
        if (sendException != null) throw sendException;
        return ValueTask.FromResult(new TunnelMessage { Wake = new WakeResponse() });
    }

    public Task StartAsync(string binPath, Speaker<ManagerMessage, TunnelMessage>.OnReceiveDelegate messageHandler,
        Speaker<ManagerMessage, TunnelMessage>.OnErrorDelegate errorHandler, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCalls++;
        return Task.CompletedTask;
    }

    public Task SendMessage(ManagerMessage message, CancellationToken ct = default)
        => throw new NotImplementedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal class FakeSystemResumeMonitor : ISystemResumeMonitor
{
    public event EventHandler? Resumed;

    public void Raise() => Resumed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}

internal class FakeManagerRpc : IManagerRpc
{
#pragma warning disable CS0067 // Never raised in tests.
    public event IManagerRpc.OnReceiveHandler? OnReceive;
#pragma warning restore CS0067

    public List<ServiceMessage> Broadcasts { get; } = [];
    public TaskCompletionSource Broadcast { get; } = new();

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    public Task BroadcastAsync(ServiceMessage message, CancellationToken ct = default)
    {
        Broadcasts.Add(message.Clone());
        Broadcast.TrySetResult();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal class FakeDownloader : IDownloader
{
    public Task<DownloadTask> StartDownloadAsync(HttpRequestMessage req, string destinationPath,
        IDownloadValidator validator, CancellationToken ct = default)
        => throw new NotImplementedException();
}

internal class FakeTelemetryEnricher : ITelemetryEnricher
{
    public StartRequest EnrichStartRequest(StartRequest original) => original;
}

[TestFixture]
public class ManagerTest
{
    private static Manager NewManager(ITunnelSupervisor tunnelSupervisor, ISystemResumeMonitor? resumeMonitor = null,
        IManagerRpc? managerRpc = null)
        => new(Options.Create(new ManagerConfig()), NullLogger<Manager>.Instance, new FakeDownloader(),
            tunnelSupervisor, managerRpc ?? new FakeManagerRpc(), new FakeTelemetryEnricher(),
            resumeMonitor ?? new FakeSystemResumeMonitor());

    private static long GetTunnelGeneration(Manager manager)
    {
        var generationField = typeof(Manager).GetField("_tunnelGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(generationField, Is.Not.Null);
        return (long)generationField!.GetValue(manager)!;
    }

    private static void RaiseTunnelRpcError(Manager manager, long tunnelGeneration, Exception error)
    {
        var handler = typeof(Manager).GetMethod("HandleTunnelRpcError", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(handler, Is.Not.Null);
        handler!.Invoke(manager, [tunnelGeneration, error]);
    }

    private static void AddPeer(Manager manager)
    {
        var handler = typeof(Manager).GetMethod("HandleTunnelMessagePeerUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(handler, Is.Not.Null);
        handler!.Invoke(manager,
        [
            new TunnelMessage
            {
                PeerUpdate = new PeerUpdate
                {
                    UpsertedWorkspaces =
                    {
                        new Workspace { Id = Google.Protobuf.ByteString.CopyFrom(new byte[16]), Name = "workspace" },
                    },
                    UpsertedAgents =
                    {
                        new Agent { Id = Google.Protobuf.ByteString.CopyFrom(new byte[16]), Name = "agent" },
                    },
                },
            },
        ]);
    }

    [Test(Description = "Send a wake request to the tunnel on system resume")]
    [CancelAfter(30_000)]
    public async Task SendsWakeRequestOnResume(CancellationToken ct)
    {
        var supervisor = new FakeTunnelSupervisor(new RpcVersion(1, 3));
        var monitor = new FakeSystemResumeMonitor();
        using var manager = NewManager(supervisor, monitor);

        monitor.Raise();
        await supervisor.Sent.Task.WaitAsync(ct);

        Assert.That(supervisor.SentRequests[0].MsgCase, Is.EqualTo(ManagerMessage.MsgOneofCase.Wake));
    }

    [TestCase(null, Description = "Tunnel not running")]
    [TestCase("1.2", Description = "Version does not support wake")]
    [CancelAfter(30_000)]
    public async Task SkipsWakeRequestWhenUnsupported(string? version, CancellationToken ct)
    {
        var supervisor = new FakeTunnelSupervisor(version == null ? null : RpcVersion.Parse(version));
        using var manager = NewManager(supervisor);

        await manager.SendWakeRequest(ct);

        Assert.That(supervisor.SentRequests, Is.Empty);
    }

    [Test(Description = "Wake request failures are swallowed")]
    [CancelAfter(30_000)]
    public async Task IgnoresWakeRequestFailure(CancellationToken ct)
    {
        var supervisor = new FakeTunnelSupervisor(new RpcVersion(1, 3), new InvalidOperationException("not running"));
        using var manager = NewManager(supervisor);

        await manager.SendWakeRequest(ct);

        Assert.That(supervisor.SentRequests, Has.Count.EqualTo(1));
    }

    [Test(Description = "Tunnel RPC errors report the VPN as stopped")]
    [CancelAfter(30_000)]
    public async Task ReportsStoppedAfterTunnelRpcError(CancellationToken ct)
    {
        var supervisor = new FakeTunnelSupervisor(new RpcVersion(1, 3));
        var managerRpc = new FakeManagerRpc();
        using var manager = NewManager(supervisor, managerRpc: managerRpc);
        AddPeer(manager);

        RaiseTunnelRpcError(manager, GetTunnelGeneration(manager), new IOException("tunnel disconnected"));
        await managerRpc.Broadcast.Task.WaitAsync(ct);

        Assert.Multiple(() =>
        {
            Assert.That(supervisor.StopCalls, Is.Zero);
            Assert.That(managerRpc.Broadcasts, Has.Count.EqualTo(1));
            Assert.That(managerRpc.Broadcasts[0].Status.Lifecycle, Is.EqualTo(Status.Types.Lifecycle.Stopped));
            Assert.That(managerRpc.Broadcasts[0].Status.PeerUpdate.UpsertedAgents, Is.Empty);
            Assert.That(managerRpc.Broadcasts[0].Status.PeerUpdate.UpsertedWorkspaces, Is.Empty);
        });
    }

    [Test(Description = "Errors from a replaced tunnel do not change VPN status")]
    [CancelAfter(30_000)]
    public async Task IgnoresErrorFromReplacedTunnel(CancellationToken ct)
    {
        var supervisor = new FakeTunnelSupervisor(new RpcVersion(1, 3));
        var managerRpc = new FakeManagerRpc();
        using var manager = NewManager(supervisor, managerRpc: managerRpc);

        RaiseTunnelRpcError(manager, GetTunnelGeneration(manager) - 1, new IOException("old tunnel disconnected"));
        await Task.Delay(100, ct);

        Assert.That(managerRpc.Broadcasts, Is.Empty);
    }

    [Test(Description = "Stopping the manager reports the VPN as stopped")]
    [CancelAfter(30_000)]
    public async Task ReportsStoppedWhenManagerStops(CancellationToken ct)
    {
        var supervisor = new FakeTunnelSupervisor(new RpcVersion(1, 3));
        var managerRpc = new FakeManagerRpc();
        using var manager = NewManager(supervisor, managerRpc: managerRpc);
        AddPeer(manager);

        await manager.StopAsync(ct);

        Assert.Multiple(() =>
        {
            Assert.That(supervisor.StopCalls, Is.EqualTo(1));
            Assert.That(managerRpc.Broadcasts, Has.Count.EqualTo(1));
            Assert.That(managerRpc.Broadcasts[0].Status.Lifecycle, Is.EqualTo(Status.Types.Lifecycle.Stopped));
            Assert.That(managerRpc.Broadcasts[0].Status.PeerUpdate.UpsertedAgents, Is.Empty);
            Assert.That(managerRpc.Broadcasts[0].Status.PeerUpdate.UpsertedWorkspaces, Is.Empty);
        });
    }
}

using Coder.Desktop.Vpn;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Coder.Desktop.Tests.Vpn.Service;

#region Fakes

internal class FakeTunnelSupervisor : ITunnelSupervisor
{
    public RpcVersion? NegotiatedVersion { get; set; }

    public List<ManagerMessage> SentRequests { get; } = [];
    public TunnelMessage? Reply { get; set; }
    public Exception? SendException { get; set; }

    public Task StartAsync(string binPath, Speaker<ManagerMessage, TunnelMessage>.OnReceiveDelegate messageHandler,
        Speaker<ManagerMessage, TunnelMessage>.OnErrorDelegate errorHandler, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task SendMessage(ManagerMessage message, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask<TunnelMessage> SendRequestAwaitReply(ManagerMessage message, CancellationToken ct = default)
    {
        SentRequests.Add(message);
        if (SendException != null) throw SendException;
        if (Reply == null) throw new InvalidOperationException("No reply configured on FakeTunnelSupervisor");
        return ValueTask.FromResult(Reply);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

internal class FakeManagerRpc : IManagerRpc
{
#pragma warning disable CS0067 // The event is only subscribed to by Manager, never raised in these tests.
    public event IManagerRpc.OnReceiveHandler? OnReceive;
#pragma warning restore CS0067

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    public Task BroadcastAsync(ServiceMessage message, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

internal class FakeDownloader : IDownloader
{
    public Task<DownloadTask> StartDownloadAsync(HttpRequestMessage req, string destinationPath,
        IDownloadValidator validator, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}

internal class FakeTelemetryEnricher : ITelemetryEnricher
{
    public StartRequest EnrichStartRequest(StartRequest original)
    {
        return original;
    }
}

#endregion

[TestFixture]
public class ManagerTest
{
    private static Manager NewManager(ITunnelSupervisor tunnelSupervisor)
    {
        return new Manager(Options.Create(new ManagerConfig()), NullLogger<Manager>.Instance, new FakeDownloader(),
            tunnelSupervisor, new FakeManagerRpc(), new FakeTelemetryEnricher());
    }

    [Test(Description = "Send a wake request to the tunnel on system resume")]
    [CancelAfter(30_000)]
    public async Task HandleSystemResumeSendsWakeRequest(CancellationToken ct)
    {
        var tunnelSupervisor = new FakeTunnelSupervisor
        {
            NegotiatedVersion = new RpcVersion(1, 3),
            Reply = new TunnelMessage
            {
                Wake = new WakeResponse
                {
                    Success = true,
                },
            },
        };
        using var manager = NewManager(tunnelSupervisor);

        await manager.HandleSystemResume(ct);

        Assert.That(tunnelSupervisor.SentRequests, Has.Count.EqualTo(1));
        Assert.That(tunnelSupervisor.SentRequests[0].MsgCase, Is.EqualTo(ManagerMessage.MsgOneofCase.Wake));
    }

    [Test(Description = "Skip the wake request when the tunnel is not running")]
    [CancelAfter(30_000)]
    public async Task HandleSystemResumeSkipsWhenTunnelNotRunning(CancellationToken ct)
    {
        var tunnelSupervisor = new FakeTunnelSupervisor
        {
            NegotiatedVersion = null,
        };
        using var manager = NewManager(tunnelSupervisor);

        await manager.HandleSystemResume(ct);

        Assert.That(tunnelSupervisor.SentRequests, Is.Empty);
    }

    [Test(Description = "Skip the wake request when the negotiated version does not support it")]
    [CancelAfter(30_000)]
    public async Task HandleSystemResumeSkipsWhenVersionTooOld(CancellationToken ct)
    {
        var tunnelSupervisor = new FakeTunnelSupervisor
        {
            NegotiatedVersion = new RpcVersion(1, 2),
        };
        using var manager = NewManager(tunnelSupervisor);

        await manager.HandleSystemResume(ct);

        Assert.That(tunnelSupervisor.SentRequests, Is.Empty);
    }

    [Test(Description = "Ignore an unexpected reply message type to a wake request")]
    [CancelAfter(30_000)]
    public async Task HandleSystemResumeIgnoresUnexpectedReply(CancellationToken ct)
    {
        var tunnelSupervisor = new FakeTunnelSupervisor
        {
            NegotiatedVersion = new RpcVersion(1, 3),
            Reply = new TunnelMessage
            {
                Stop = new StopResponse(),
            },
        };
        using var manager = NewManager(tunnelSupervisor);

        // HandleSystemResume must not throw when the tunnel replies with an
        // unexpected message type.
        await manager.HandleSystemResume(ct);

        Assert.That(tunnelSupervisor.SentRequests, Has.Count.EqualTo(1));
    }

    [Test(Description = "Ignore a wake request send failure without affecting the tunnel")]
    [CancelAfter(30_000)]
    public async Task HandleSystemResumeIgnoresSendFailure(CancellationToken ct)
    {
        var tunnelSupervisor = new FakeTunnelSupervisor
        {
            NegotiatedVersion = new RpcVersion(1, 3),
            SendException = new InvalidOperationException("TunnelSupervisor is not running"),
        };
        using var manager = NewManager(tunnelSupervisor);

        // HandleSystemResume must not throw when sending the wake request
        // fails.
        await manager.HandleSystemResume(ct);

        Assert.That(tunnelSupervisor.SentRequests, Has.Count.EqualTo(1));
    }
}

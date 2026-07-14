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

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    public Task BroadcastAsync(ServiceMessage message, CancellationToken ct = default) => Task.CompletedTask;
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
    private static Manager NewManager(ITunnelSupervisor tunnelSupervisor, ISystemResumeMonitor? resumeMonitor = null)
        => new(Options.Create(new ManagerConfig()), NullLogger<Manager>.Instance, new FakeDownloader(),
            tunnelSupervisor, new FakeManagerRpc(), new FakeTelemetryEnricher(),
            resumeMonitor ?? new FakeSystemResumeMonitor());

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
}

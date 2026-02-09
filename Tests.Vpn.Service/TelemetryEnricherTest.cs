using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Service;

namespace Coder.Desktop.Tests.Vpn.Service;

[TestFixture]
public class TelemetryEnricherTest
{
    [Test]
    public void EnrichStartRequest()
    {
        var req = new StartRequest
        {
            CoderUrl = "https://coder.example.com",
        };
        var enricher = new TelemetryEnricher();
        req = enricher.EnrichStartRequest(req);

        // quick sanity check that non-telemetry fields aren't lost or overwritten
        Assert.That(req.CoderUrl, Is.EqualTo("https://coder.example.com"));

        var expectedOs = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        Assert.That(req.DeviceOs, Is.EqualTo(expectedOs));
        // seems that test assemblies always set 1.0.0.0
        Assert.That(req.CoderDesktopVersion, Is.EqualTo("1.0.0.0"));
        // DeviceId may be empty on some Linux CI environments without /etc/machine-id
        if (OperatingSystem.IsWindows() || File.Exists("/etc/machine-id"))
            Assert.That(req.DeviceId, Is.Not.Empty);
        var deviceId = req.DeviceId;

        // deviceId is different on different machines, but we can test that
        // each instance of the TelemetryEnricher produces the same value.
        enricher = new TelemetryEnricher();
        req = enricher.EnrichStartRequest(new StartRequest());
        Assert.That(req.DeviceId, Is.EqualTo(deviceId));
    }
}

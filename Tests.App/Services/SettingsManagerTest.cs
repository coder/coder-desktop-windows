using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;

namespace Coder.Desktop.Tests.App.Services;
[TestFixture]
public sealed class SettingsManagerTests
{
    private string _tempDir = string.Empty;
    private SettingsManager<CoderConnectSettings> _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _sut = new SettingsManager<CoderConnectSettings>(_tempDir); // inject isolated path
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    [Test]
    public void Save_Persists()
    {
        var expected = true;
        var settings = new CoderConnectSettings
        {
            Version = 1,
            ConnectOnLaunch = expected
        };
        _sut.Write(settings).GetAwaiter().GetResult();
        var actual = _sut.Read().GetAwaiter().GetResult();
        Assert.That(actual.ConnectOnLaunch, Is.EqualTo(expected));
    }

    [Test]
    public void Read_MissingKey_ReturnsDefault()
    {
        var actual = _sut.Read().GetAwaiter().GetResult();
        Assert.That(actual.ConnectOnLaunch, Is.False);
    }
}

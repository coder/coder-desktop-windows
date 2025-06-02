using Coder.Desktop.App.Services;

namespace Coder.Desktop.Tests.App.Services;
[TestFixture]
public sealed class SettingsManagerTests
{
    private string _tempDir = string.Empty;
    private SettingsManager _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _sut = new SettingsManager(_tempDir); // inject isolated path
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
    }

    [Test]
    public void Save_Persists()
    {
        bool expected = true;
        _sut.StartOnLogin = expected;

        Assert.That(_sut.StartOnLogin, Is.EqualTo(expected));
    }

    [Test]
    public void Read_MissingKey_ReturnsDefault()
    {
        bool result = _sut.ConnectOnLaunch; // default is false
        Assert.That(result, Is.False);
    }

    [Test]
    public void Read_AfterReload_ReturnsPreviouslySavedValue()
    {
        const bool value = true;

        _sut.ConnectOnLaunch = value;

        // Create new instance to force file reload.
        var newManager = new SettingsManager(_tempDir);
        bool persisted = newManager.ConnectOnLaunch;

        Assert.That(persisted, Is.EqualTo(value));
    }
}

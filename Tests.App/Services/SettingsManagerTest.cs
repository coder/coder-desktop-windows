using Coder.Desktop.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Coder.Desktop.Tests.App.Services
{
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
        public void Save_ReturnsValue_AndPersists()
        {
            int expected = 42;
            int actual = _sut.Save("Answer", expected);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(_sut.Read("Answer", -1), Is.EqualTo(expected));
        }

        [Test]
        public void Read_MissingKey_ReturnsDefault()
        {
            bool result = _sut.Read("DoesNotExist", defaultValue: false);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Read_AfterReload_ReturnsPreviouslySavedValue()
        {
            const string key = "Greeting";
            const string value = "Hello";

            _sut.Save(key, value);

            // Create new instance to force file reload.
            var newManager = new SettingsManager(_tempDir);
            string persisted = newManager.Read(key, string.Empty);

            Assert.That(persisted, Is.EqualTo(value));
        }
    }
}

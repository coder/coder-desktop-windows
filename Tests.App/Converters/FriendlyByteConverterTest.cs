using Coder.Desktop.App.Converters;

namespace Coder.Desktop.Tests.App.Converters;

[TestFixture]
public class FriendlyByteConverterTest
{
    [Test]
    public void EndToEnd()
    {
        var cases = new List<(object, string)>
        {
            (0, "0 B"),
            ((uint)0, "0 B"),
            ((long)0, "0 B"),
            ((ulong)0, "0 B"),

            (1, "1 B"),
            (1024, "1 KB"),
            ((ulong)(1.1 * 1024), "1.1 KB"),
            (1024 * 1024, "1 MB"),
            (1024 * 1024 * 1024, "1 GB"),
            ((ulong)1024 * 1024 * 1024 * 1024, "1 TB"),
            ((ulong)1024 * 1024 * 1024 * 1024 * 1024, "1 PB"),
            ((ulong)1024 * 1024 * 1024 * 1024 * 1024 * 1024, "1 EB"),
            (ulong.MaxValue, "16 EB"),
        };

        var converter = new FriendlyByteConverter();
        foreach (var (input, expected) in cases)
        {
            var actual = converter.Convert(input, typeof(string), null, null);
            Assert.That(actual, Is.EqualTo(expected), $"case ({input.GetType()}){input}");
        }
    }
}

using Coder.Desktop.CoderSdk;

namespace Coder.Desktop.Tests.CoderSdk;

[TestFixture]
public class UuidTest
{
    private const string UuidStr = "df762f71-898c-44a2-84c6-8add83704266";

    private static readonly byte[] UuidBytes =
        [0xdf, 0x76, 0x2f, 0x71, 0x89, 0x8c, 0x44, 0xa2, 0x84, 0xc6, 0x8a, 0xdd, 0x83, 0x70, 0x42, 0x66];

    [Test(Description = "Convert UUID bytes => Uuid")]
    public void BytesToUuid()
    {
        var uuid = new Uuid(UuidBytes);
        Assert.That(uuid.ToString(), Is.EqualTo(UuidStr));
        Assert.That(uuid.Bytes.ToArray(), Is.EqualTo(UuidBytes));
    }

    [Test(Description = "Convert UUID string => Uuid")]
    public void StringToUuid()
    {
        var uuid = new Uuid(UuidStr);
        Assert.That(uuid.ToString(), Is.EqualTo(UuidStr));
        Assert.That(uuid.Bytes.ToArray(), Is.EqualTo(UuidBytes));
    }

    [Test(Description = "Convert capitalized UUID string => Uuid")]
    public void CapitalizedStringToUuid()
    {
        var uuid = new Uuid(UuidStr.ToUpper());
        // The capitalized string should be discarded after parsing.
        Assert.That(uuid.ToString(), Is.EqualTo(UuidStr));
        Assert.That(uuid.Bytes.ToArray(), Is.EqualTo(UuidBytes));
    }

    [Test(Description = "Invalid length")]
    public void InvalidLength()
    {
        var ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(Array.Empty<byte>()));
        Assert.That(ex.Message, Does.Contain("UUID must be 16 bytes, but was 0 bytes"));
        ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(UuidBytes.AsSpan(..^1)));
        Assert.That(ex.Message, Does.Contain("UUID must be 16 bytes, but was 15 bytes"));
        var longerBytes = UuidBytes.Append((byte)0x0).ToArray();
        ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(longerBytes));
        Assert.That(ex.Message, Does.Contain("UUID must be 16 bytes, but was 17 bytes"));

        ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(""));
        Assert.That(ex.Message, Does.Contain("UUID string must be 36 characters, but was 0 characters"));
        ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(UuidStr[..^1]));
        Assert.That(ex.Message, Does.Contain("UUID string must be 36 characters, but was 35 characters"));
        ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(UuidStr + "0"));
        Assert.That(ex.Message, Does.Contain("UUID string must be 36 characters, but was 37 characters"));
    }

    [Test(Description = "Invalid version")]
    public void InvalidVersion()
    {
        var invalidVersionBytes = new byte[16];
        Array.Copy(UuidBytes, invalidVersionBytes, UuidBytes.Length);
        invalidVersionBytes[6] = (byte)(invalidVersionBytes[6] & 0x0f); // clear version nibble
        var ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(invalidVersionBytes));
        Assert.That(ex.Message, Does.Contain("ID does not seem like a valid UUIDv4"));

        var invalidVersionChars = UuidStr.ToCharArray();
        invalidVersionChars[14] = '0'; // clear version nibble
        ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(new string(invalidVersionChars)));
        Assert.That(ex.Message, Does.Contain("ID does not seem like a valid UUIDv4"));
    }

    [Test(Description = "Invalid string")]
    public void InvalidString()
    {
        var hyphenMissing = UuidStr.Replace("-", "0");
        var ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(hyphenMissing));
        Assert.That(ex.Message, Does.Contain("UUID string must have dashes at positions 8, 13, 18, and 23"));

        var invalidHex = UuidStr.ToCharArray();
        invalidHex[2] = 'g'; // invalid hex digit
        ex = Assert.Throws<ArgumentException>(() => _ = new Uuid(new string(invalidHex)));
        Assert.That(ex.Message, Does.Contain("UUID string has invalid hex digits at position 2"));
    }

    [Test(Description = "Try methods")]
    public void Try()
    {
        Assert.That(Uuid.TryFrom(UuidBytes, out var uuid1), Is.True);
        Assert.That(uuid1.Bytes.ToArray(), Is.EqualTo(UuidBytes));

        Assert.That(Uuid.TryFrom([], out var uuid2), Is.False);
        Assert.That(uuid2, Is.EqualTo(Uuid.Zero));

        Assert.That(Uuid.TryParse(UuidStr, out var uuid3), Is.True);
        Assert.That(uuid3.ToString(), Is.EqualTo(UuidStr));

        Assert.That(Uuid.TryParse("", out var uuid4), Is.False);
        Assert.That(uuid4, Is.EqualTo(Uuid.Zero));
    }

    [Test(Description = "Equality")]
    public void Equality()
    {
        var differentUuidBytes = new byte[16];
        Array.Copy(UuidBytes, differentUuidBytes, UuidBytes.Length);
        differentUuidBytes[0] = (byte)(differentUuidBytes[0] + 1);

        var uuid1 = new Uuid(UuidBytes);
        var uuid2 = new Uuid(UuidBytes);
        var uuid3 = new Uuid(differentUuidBytes);

#pragma warning disable CS1718 // Comparison made to same variable
#pragma warning disable NUnit2010 // Use Is.EqualTo constraint instead of direct comparison
        // ReSharper disable EqualExpressionComparison
        Assert.That(uuid1 == uuid1, Is.True);
        Assert.That(uuid1 != uuid1, Is.False);
        Assert.That(Uuid.Zero == Uuid.Zero, Is.True);
        Assert.That(Uuid.Zero != Uuid.Zero, Is.False);
        // ReSharper restore EqualExpressionComparison
#pragma warning restore NUnit2010
#pragma warning restore CS1718

        Assert.That(uuid1 == uuid2, Is.True);
        Assert.That(uuid2 == uuid1, Is.True);
        Assert.That(uuid1 != uuid2, Is.False);
        Assert.That(uuid2 != uuid1, Is.False);

        Assert.That(uuid1 == uuid3, Is.False);
        Assert.That(uuid3 == uuid1, Is.False);
        Assert.That(uuid1 != uuid3, Is.True);
        Assert.That(uuid3 != uuid1, Is.True);

        Assert.That(uuid1.Equals(uuid2), Is.True);
        Assert.That(uuid2.Equals(uuid1), Is.True);
        Assert.That(uuid1.Equals(uuid3), Is.False);
        Assert.That(uuid3.Equals(uuid1), Is.False);

        Assert.That(uuid1.Equals(null!), Is.False);
        Assert.That(uuid1.Equals(new object()), Is.False);
    }
}

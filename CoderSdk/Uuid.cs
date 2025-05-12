using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coder.Desktop.CoderSdk;

/// <summary>
///     A simplistic UUIDv4 class that wraps a 16-byte array. We don't use the
///     native Guid class because it has some weird reordering behavior due to
///     legacy Windows stuff. This class is not guaranteed to provide full RFC
///     4122 compliance, but it should provide enough coverage for Coder
///     Desktop.
/// </summary>
[JsonConverter(typeof(UuidJsonConverter))]
public class Uuid
{
    private readonly byte[] _bytes;

    /// <summary>
    ///     The (invalid) zero UUID.
    /// </summary>
    public static Uuid Zero { get; } = new();

    public ReadOnlySpan<byte> Bytes => _bytes;

    private Uuid()
    {
        _bytes = new byte[16];
    }

    public Uuid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException($"UUID must be 16 bytes, but was {bytes.Length} bytes", nameof(bytes));
        if (bytes[6] >> 4 != 0x4)
            throw new ArgumentException("ID does not seem like a valid UUIDv4", nameof(bytes));
        _bytes = bytes.ToArray();
    }

    public Uuid(string str)
    {
        if (str.Length != 36)
            throw new ArgumentException($"UUID string must be 36 characters, but was {str.Length} characters",
                nameof(str));

        var currentIndex = 0;
        _bytes = new byte[16];

        for (var i = 0; i < 36; i++)
        {
            if (i is 8 or 13 or 18 or 23)
            {
                if (str[i] != '-')
                    throw new ArgumentException("UUID string must have dashes at positions 8, 13, 18, and 23",
                        nameof(str));
                continue;
            }

            // Take two hex digits and convert them to a byte.
            var hex = str[i..(i + 2)];
            if (!byte.TryParse(hex, NumberStyles.HexNumber, null, out var b))
                throw new ArgumentException($"UUID string has invalid hex digits at position {i}", nameof(str));
            _bytes[currentIndex] = b;
            currentIndex++;

            // Advance the loop index by 1 as we processed two characters.
            i++;
        }

        if (currentIndex != 16)
            throw new ArgumentException($"UUID string must have 16 bytes, but was {currentIndex} bytes", nameof(str));
        if (_bytes[6] >> 4 != 0x4)
            throw new ArgumentException("ID does not seem like a valid UUIDv4", nameof(str));
    }

    public static bool TryFrom(ReadOnlySpan<byte> bytes, out Uuid uuid)
    {
        try
        {
            uuid = new Uuid(bytes);
            return true;
        }
        catch
        {
            uuid = Zero;
            return false;
        }
    }

    public static bool TryParse(string str, out Uuid uuid)
    {
        try
        {
            uuid = new Uuid(str);
            return true;
        }
        catch
        {
            uuid = Zero;
            return false;
        }
    }

    public override string ToString()
    {
        if (_bytes.Length != 16)
            throw new ArgumentException($"UUID must be 16 bytes, but was {_bytes.Length} bytes", nameof(_bytes));

        // Print every byte as hex, with dashes in the right places.
        var sb = new StringBuilder(36);
        for (var i = 0; i < 16; i++)
        {
            if (i is 4 or 6 or 8 or 10)
                sb.Append('-');
            sb.Append(_bytes[i].ToString("x2"));
        }

        return sb.ToString();
    }

    #region Uuid equality

    public override bool Equals(object? obj)
    {
        return obj is Uuid other && Equals(other);
    }

    public bool Equals(Uuid? other)
    {
        return other is not null && _bytes.SequenceEqual(other._bytes);
    }

    public override int GetHashCode()
    {
        return _bytes.GetHashCode();
    }

    public static bool operator ==(Uuid left, Uuid right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Uuid left, Uuid right)
    {
        return !left.Equals(right);
    }

    #endregion
}

public class UuidJsonConverter : JsonConverter<Uuid>
{
    public override Uuid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string token type for UUID");

        var str = reader.GetString();
        if (str == null)
            return null;

        try
        {
            return new Uuid(str);
        }
        catch (Exception ex)
        {
            throw new JsonException($"Invalid UUID string '{str}'", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Uuid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

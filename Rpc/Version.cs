namespace Coder.Desktop.Rpc;

/// <summary>
///     Thrown when the two peers are incompatible with each other.
/// </summary>
public class ApiCompatibilityException(ApiVersion localVersion, ApiVersion remoteVersion, string message)
    : Exception($"{message}: local={localVersion}, remote={remoteVersion}");

/// <summary>
///     A version of the RPC API. Can be compared other versions to determine compatibility between two peers.
/// </summary>
/// <param name="major">The major version of the peer</param>
/// <param name="minor">The minor version of the peer</param>
/// <param name="additionalMajors">Additional supported major versions of the peer</param>
public class ApiVersion(int major, int minor, params int[] additionalMajors)
{
    public static readonly ApiVersion Current = new(1, 0);

    public int Major { get; } = major;
    public int Minor { get; } = minor;
    public int[] AdditionalMajors { get; } = additionalMajors;

    /// <summary>
    ///     Parse a string in the format "major.minor" into an ApiVersion.
    /// </summary>
    /// <param name="versionString">Version string to parse</param>
    /// <returns>Parsed ApiVersion</returns>
    /// <exception cref="ArgumentException">The version string is invalid</exception>
    public static ApiVersion ParseString(string versionString)
    {
        var parts = versionString.Split('.');
        if (parts.Length != 2) throw new ArgumentException($"Invalid version string '{versionString}'");

        try
        {
            var major = int.Parse(parts[0]);
            var minor = int.Parse(parts[1]);
            return new ApiVersion(major, minor);
        }
        catch (FormatException e)
        {
            throw new ArgumentException($"Invalid version string '{versionString}'", e);
        }
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }

    /// <summary>
    ///     Validate that this version is compatible with another version. If the other version is not compatible, an exception
    ///     is thrown.
    /// </summary>
    /// <param name="other">Version to compare against</param>
    /// <exception cref="ApiCompatibilityException">The two peers have incompatible versions</exception>
    public void Validate(ApiVersion other)
    {
        if (other.Major > Major) throw new ApiCompatibilityException(this, other, "Peer supports newer major version");
        if (other.Major == Major)
        {
            if (other.Minor > Minor)
                throw new ApiCompatibilityException(this, other, "Peer supports newer minor version");

            return;
        }

        if (AdditionalMajors.Any(major => other.Major == major)) return;
        throw new ApiCompatibilityException(this, other, "Version is no longer supported");
    }
}
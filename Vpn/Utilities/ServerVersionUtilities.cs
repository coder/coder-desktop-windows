using Semver;

namespace Coder.Desktop.Vpn.Utilities;

public class ServerVersion
{
    public required string RawString { get; set; }
    public required SemVersion SemVersion { get; set; }
}

public static class ServerVersionUtilities
{
    // The -0 allows pre-release versions.
    private static readonly SemVersionRange ServerVersionRange = SemVersionRange.Parse(">= 2.20.0-0",
        SemVersionRangeOptions.IncludeAllPrerelease | SemVersionRangeOptions.AllowV |
        SemVersionRangeOptions.AllowMetadata);

    /// <summary>
    ///     Attempts to parse and verify that the server version is within the supported range.
    /// </summary>
    /// <param name="versionString">
    ///     The server version to check, optionally with a leading `v` or extra metadata/pre-release
    ///     tags
    /// </param>
    /// <returns>The parsed server version</returns>
    /// <exception cref="ArgumentException">Could not parse version</exception>
    /// <exception cref="ArgumentException">The server version is not in range</exception>
    public static ServerVersion ParseAndValidateServerVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
            throw new ArgumentException("Server version is empty", nameof(versionString));
        if (!SemVersion.TryParse(versionString, SemVersionStyles.AllowV, out var version))
            throw new ArgumentException($"Could not parse server version '{versionString}'", nameof(versionString));
        if (!version.Satisfies(ServerVersionRange))
            throw new ArgumentException(
                $"Server version '{version}' is not within required server version range '{ServerVersionRange}'",
                nameof(versionString));

        return new ServerVersion
        {
            RawString = versionString,
            SemVersion = version,
        };
    }
}

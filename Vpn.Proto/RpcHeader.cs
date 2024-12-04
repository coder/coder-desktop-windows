using System.Text;

namespace Coder.Desktop.Vpn.Proto;

/// <summary>
///     A header to write or read from a stream to identify the speaker's role and version.
/// </summary>
/// <param name="role">Role of the speaker</param>
/// <param name="versionList">Version of the speaker</param>
public class RpcHeader(RpcRole role, RpcVersionList versionList)
{
    private const string Preamble = "codervpn";

    public RpcRole Role { get; } = role;
    public RpcVersionList VersionList { get; } = versionList;

    /// <summary>
    ///     Parse a header string into a <c>SpeakerHeader</c>.
    /// </summary>
    /// <param name="header">Raw header string without trailing newline</param>
    /// <returns>Parsed header</returns>
    /// <exception cref="ArgumentException">Invalid header string</exception>
    public static RpcHeader Parse(string header)
    {
        var parts = header.Split(' ');
        if (parts.Length != 3) throw new ArgumentException($"Wrong number of parts in header string '{header}'");
        if (parts[0] != Preamble) throw new ArgumentException($"Invalid preamble in header string '{header}'");

        var role = new RpcRole(parts[1]);
        var versionList = RpcVersionList.Parse(parts[2]);
        return new RpcHeader(role, versionList);
    }

    /// <summary>
    ///     Construct a header string from the role and version with a trailing newline.
    /// </summary>
    public override string ToString()
    {
        return $"{Preamble} {Role} {VersionList}\n";
    }

    public ReadOnlyMemory<byte> ToBytes()
    {
        return Encoding.UTF8.GetBytes(ToString());
    }
}

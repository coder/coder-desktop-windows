using System.Text;

namespace Coder.Desktop.Vpn.Proto;

/// <summary>
///     A header to write or read from a stream to identify the peer role and version.
/// </summary>
public class RpcHeader
{
    private const string Preamble = "codervpn";

    public string Role { get; }
    public RpcVersionList VersionList { get; }

    /// <param name="role">Role of the peer</param>
    /// <param name="versionList">Version of the peer</param>
    public RpcHeader(string role, RpcVersionList versionList)
    {
        Role = role;
        VersionList = versionList;
    }

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
        if (string.IsNullOrEmpty(parts[1])) throw new ArgumentException($"Invalid role in header string '{header}'");

        var versionList = RpcVersionList.Parse(parts[2]);
        return new RpcHeader(parts[1], versionList);
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

using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Coder.Desktop.Vpn;

[SupportedOSPlatform("linux")]
public class UnixSocketClientTransport : IRpcClientTransport
{
    private readonly string _socketPath;

    public UnixSocketClientTransport(string socketPath = "/run/coder-desktop/vpn.sock")
    {
        _socketPath = socketPath;
    }

    public async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

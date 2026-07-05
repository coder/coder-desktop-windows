using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Coder.Desktop.Vpn;

[SupportedOSPlatform("linux")]
public class UnixSocketClientTransport : IRpcClientTransport
{
    private readonly string _socketPath;

    public UnixSocketClientTransport(string socketPath = UnixSocketServerTransport.DefaultSocketPath)
    {
        var envSocketPath = Environment.GetEnvironmentVariable("CODER_DESKTOP_RPC_SOCKET_PATH");
        _socketPath = string.IsNullOrWhiteSpace(envSocketPath) ? socketPath : envSocketPath;
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

using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Coder.Desktop.Vpn;

[SupportedOSPlatform("linux")]
public class UnixSocketServerTransport : IRpcServerTransport
{
    private readonly string _socketPath;
    private Socket? _listener;

    public UnixSocketServerTransport(string socketPath = "/run/coder-desktop/vpn.sock")
    {
        _socketPath = socketPath;
    }

    public async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        if (_listener == null)
        {
            // Clean up stale socket file
            if (File.Exists(_socketPath))
                File.Delete(_socketPath);

            // Ensure parent directory exists
            var dir = Path.GetDirectoryName(_socketPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));

            // Set permissions so all users can connect (equivalent to WorldSid on Windows)
            File.SetUnixFileMode(_socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite);

            _listener.Listen(5);
        }

        var client = await _listener.AcceptAsync(ct);
        return new NetworkStream(client, ownsSocket: true);
    }

    public ValueTask DisposeAsync()
    {
        if (_listener != null)
        {
            _listener.Close();
            _listener.Dispose();
            _listener = null;
        }

        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { /* best effort */ }
        }

        return ValueTask.CompletedTask;
    }
}

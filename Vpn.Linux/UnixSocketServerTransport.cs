using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Coder.Desktop.Vpn;

[SupportedOSPlatform("linux")]
public class UnixSocketServerTransport : IRpcServerTransport
{
    public const string DefaultSocketPath = "/run/coder-desktop/vpn.sock";

    private readonly string _socketPath;
    private Socket? _listener;

    public UnixSocketServerTransport(string socketPath = DefaultSocketPath)
    {
        _socketPath = socketPath;
    }

    public async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        _listener ??= CreateListener();
        var client = await _listener.AcceptAsync(ct);
        return new NetworkStream(client, ownsSocket: true);
    }

    private Socket CreateListener()
    {
        // Only remove an existing path if it is a stale socket, never a
        // regular file, since the path is configurable.
        if (File.Exists(_socketPath))
        {
            if (new FileInfo(_socketPath).Length > 0)
                throw new InvalidOperationException(
                    $"Refusing to replace existing non-socket file at RPC socket path '{_socketPath}'");
            File.Delete(_socketPath);
        }

        var dir = Path.GetDirectoryName(_socketPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath));

            // Allow all users to connect (equivalent to WorldSid on Windows).
            File.SetUnixFileMode(_socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite);

            listener.Listen(5);
            return listener;
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_listener != null)
        {
            _listener.Close();
            _listener.Dispose();
            _listener = null;

            try
            {
                File.Delete(_socketPath);
            }
            catch
            {
                // Best effort cleanup of the socket file.
            }
        }

        return ValueTask.CompletedTask;
    }
}

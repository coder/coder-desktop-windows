using System.Net.Sockets;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Single instance enforcement using Unix domain socket lock.
/// </summary>
public class LinuxSingleInstance : IDisposable
{
    private Socket? _lockSocket;
    private readonly string _socketPath;

    public LinuxSingleInstance(string? appName = null)
    {
        var name = appName ?? "coder-desktop";
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? Path.Combine(Path.GetTempPath(), $"coder-{Environment.UserName}");
        _socketPath = Path.Combine(runtimeDir, $"{name}.lock");
    }

    public bool TryAcquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_socketPath)!);

        _lockSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            // Try to detect stale socket from a crashed instance
            if (File.Exists(_socketPath))
            {
                using var testSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    testSocket.Connect(new UnixDomainSocketEndPoint(_socketPath));
                    // Connected — another instance is running
                    testSocket.Close();
                    _lockSocket.Dispose();
                    _lockSocket = null;
                    return false;
                }
                catch (SocketException)
                {
                    // Failed to connect — stale socket, remove it
                    File.Delete(_socketPath);
                }
            }

            _lockSocket!.Bind(new UnixDomainSocketEndPoint(_socketPath));
            _lockSocket!.Listen(1);
            return true;
        }
        catch (SocketException)
        {
            _lockSocket?.Dispose();
            _lockSocket = null;
            return false;
        }
    }

    public void Dispose()
    {
        if (_lockSocket != null)
        {
            _lockSocket.Close();
            _lockSocket.Dispose();
            _lockSocket = null;
        }

        try
        {
            if (File.Exists(_socketPath))
                File.Delete(_socketPath);
        }
        catch
        {
            // best effort
        }
        GC.SuppressFinalize(this);
    }
}

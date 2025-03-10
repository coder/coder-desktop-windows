using Coder.Desktop.MutagenSdk.Proto.Service.Synchronization;
using Grpc.Core;
using Grpc.Net.Client;

namespace Coder.Desktop.MutagenSdk;

public class MutagenClient : IDisposable
{
    private readonly GrpcChannel _channel;

    public Synchronization.SynchronizationClient Client { get; }

    public MutagenClient(string dataDir)
    {
        // Check for the lock file first, since it should exist if it's running.
        var daemonLockFile = Path.Combine(dataDir, "daemon", "daemon.lock");
        if (!File.Exists(daemonLockFile))
            throw new FileNotFoundException(
                "Mutagen daemon lock file not found, did the mutagen daemon start successfully?", daemonLockFile);

        // Read the IPC named pipe address from the sock file.
        var daemonSockFile = Path.Combine(dataDir, "daemon", "daemon.sock");
        if (!File.Exists(daemonSockFile))
            throw new FileNotFoundException(
                "Mutagen daemon socket file not found, did the mutagen daemon start successfully?", daemonSockFile);
        var daemonSockAddress = File.ReadAllText(daemonSockFile).Trim();
        if (string.IsNullOrWhiteSpace(daemonSockAddress))
            throw new InvalidOperationException(
                "Mutagen daemon socket address is empty, did the mutagen daemon start successfully?");

        const string namedPipePrefix = @"\\.\pipe\";
        if (!daemonSockAddress.StartsWith(namedPipePrefix))
            throw new InvalidOperationException("Mutagen daemon socket address is not a named pipe address");
        var pipeName = daemonSockAddress[namedPipePrefix.Length..];

        var connectionFactory = new NamedPipesConnectionFactory(pipeName);
        var socketsHttpHandler = new SocketsHttpHandler
        {
            ConnectCallback = connectionFactory.ConnectAsync,
        };

        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            HttpHandler = socketsHttpHandler,
        });
        Client = new Synchronization.SynchronizationClient(_channel);
    }

    public void Dispose()
    {
        _channel.Dispose();
        GC.SuppressFinalize(this);
    }
}

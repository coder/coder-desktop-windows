using Coder.Desktop.MutagenSdk.Proto.Service.Daemon;
using Coder.Desktop.MutagenSdk.Proto.Service.Prompting;
using Coder.Desktop.MutagenSdk.Proto.Service.Synchronization;
using Grpc.Core;
using Grpc.Net.Client;

namespace Coder.Desktop.MutagenSdk;

public class MutagenClient : IDisposable
{
    private readonly GrpcChannel _channel;

    public readonly Daemon.DaemonClient Daemon;
    public readonly Prompting.PromptingClient Prompting;
    public readonly Synchronization.SynchronizationClient Synchronization;

    public MutagenClient(string dataDir)
    {
        // Read the IPC named pipe address from the sock file.
        var daemonSockFile = Path.Combine(dataDir, "daemon", "daemon.sock");
        if (!File.Exists(daemonSockFile))
            throw new FileNotFoundException(
                "Mutagen daemon socket file not found, did the mutagen daemon start successfully?", daemonSockFile);
        var daemonSockAddress = File.ReadAllText(daemonSockFile).Trim();
        if (string.IsNullOrWhiteSpace(daemonSockAddress))
            throw new InvalidOperationException(
                $"Mutagen daemon socket address from '{daemonSockFile}' is empty, did the mutagen daemon start successfully?");

        const string namedPipePrefix = @"\\.\pipe\";
        if (!daemonSockAddress.StartsWith(namedPipePrefix) || daemonSockAddress == namedPipePrefix)
            throw new InvalidOperationException(
                $"Mutagen daemon socket address '{daemonSockAddress}' is not a valid named pipe address");

        // Ensure the pipe exists before we try to connect to it. Obviously
        // this is not 100% foolproof, since the pipe could appear/disappear
        // after we check it. This allows us to fail early if the pipe isn't
        // ready yet (and consumers can retry), otherwise the pipe connection
        // may block.
        //
        // Note: we cannot use File.Exists here without breaking the named
        // pipe connection code due to a .NET bug.
        // https://github.com/dotnet/runtime/issues/69604
        var pipeName = daemonSockAddress[namedPipePrefix.Length..];
        var foundPipe = Directory
            .GetFiles(namedPipePrefix, pipeName)
            .FirstOrDefault(p => Path.GetFileName(p) == pipeName);
        if (foundPipe == null)
            throw new FileNotFoundException(
                "Mutagen daemon named pipe not found, did the mutagen daemon start successfully?", daemonSockAddress);

        var connectionFactory = new NamedPipesConnectionFactory(pipeName);
        var socketsHttpHandler = new SocketsHttpHandler
        {
            ConnectCallback = connectionFactory.ConnectAsync,
        };

        // http://localhost is fake address. The HttpHandler will be used to
        // open a socket to the named pipe.
        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            HttpHandler = socketsHttpHandler,
        });

        Daemon = new Daemon.DaemonClient(_channel);
        Prompting = new Prompting.PromptingClient(_channel);
        Synchronization = new Synchronization.SynchronizationClient(_channel);
    }

    public void Dispose()
    {
        _channel.Dispose();
        GC.SuppressFinalize(this);
    }
}

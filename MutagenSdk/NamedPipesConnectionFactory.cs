using System.IO.Pipes;
using System.Security.Principal;

namespace Coder.Desktop.MutagenSdk;

public class NamedPipesConnectionFactory
{
    private readonly string _pipeName;

    public NamedPipesConnectionFactory(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext _,
        CancellationToken cancellationToken = default)
    {
        var client = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.WriteThrough | PipeOptions.Asynchronous,
            TokenImpersonationLevel.Anonymous);

        try
        {
            // Set an upper limit of 2.5 seconds. MutagenSdk consumers can
            // retry if necessary.
            await client.ConnectAsync(2500, cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}

using System.IO.Pipes;

namespace Coder.Desktop.Vpn;

public class NamedPipeClientTransport : IRpcClientTransport
{
    private readonly string _pipeName;

    public NamedPipeClientTransport(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", _pipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(ct);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }
}

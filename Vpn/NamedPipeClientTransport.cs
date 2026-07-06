using System.IO.Pipes;
using System.Runtime.Versioning;

namespace Coder.Desktop.Vpn;

[SupportedOSPlatform("windows")]
public class NamedPipeClientTransport : IRpcClientTransport
{
    public const string DefaultPipeName = "Coder.Desktop.Vpn";

    private readonly string _pipeName;

    public NamedPipeClientTransport(string pipeName = DefaultPipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(ct);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}

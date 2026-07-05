using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Coder.Desktop.Vpn;

[SupportedOSPlatform("windows")]
public class NamedPipeServerTransport : IRpcServerTransport
{
    private readonly string _pipeName;

    public NamedPipeServerTransport(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        // Allow everyone to connect to the named pipe.
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Starting a named pipe server is not like a TCP server where you can
        // continuously accept new connections. You need to recreate the server
        // after accepting a connection in order to accept new connections.
        var pipeServer = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0,
            0, pipeSecurity);

        try
        {
            await pipeServer.WaitForConnectionAsync(ct);
            return pipeServer;
        }
        catch
        {
            await pipeServer.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

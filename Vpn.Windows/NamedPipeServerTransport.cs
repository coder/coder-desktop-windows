using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Coder.Desktop.Vpn;

public class NamedPipeServerTransport : IRpcServerTransport
{
    private readonly string _pipeName;

    public NamedPipeServerTransport(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        var pipe = NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity);
        try
        {
            await pipe.WaitForConnectionAsync(ct);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

namespace Coder.Desktop.Vpn;

/// <summary>
/// Server-side transport for accepting RPC connections.
/// Windows: Named Pipes. Linux: Unix Domain Sockets.
/// </summary>
public interface IRpcServerTransport : IAsyncDisposable
{
    /// <summary>Accept a single client connection, returning a bidirectional stream.</summary>
    Task<Stream> AcceptAsync(CancellationToken ct);
}

/// <summary>
/// Client-side transport for connecting to the RPC server.
/// </summary>
public interface IRpcClientTransport
{
    /// <summary>Connect to the RPC server, returning a bidirectional stream.</summary>
    Task<Stream> ConnectAsync(CancellationToken ct);
}

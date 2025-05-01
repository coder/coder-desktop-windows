using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.App.Services;

public struct RdpCredentials(string username, string password)
{
    public readonly string Username = username;
    public readonly string Password = password;
}

public interface IRdpConnector : IAsyncDisposable
{
    public const int DefaultPort = 3389;

    public Task WriteCredentials(string fqdn, RdpCredentials credentials, CancellationToken ct = default);

    public Task Connect(string fqdn, int port = DefaultPort, CancellationToken ct = default);
}

public class RdpConnector(ILogger<RdpConnector> logger) : IRdpConnector
{
    // Remote Desktop always uses TERMSRV as the domain; RDP is a part of Windows "Terminal Services".
    private const string RdpDomain = "TERMSRV";

    public Task WriteCredentials(string fqdn, RdpCredentials credentials, CancellationToken ct = default)
    {
        // writing credentials is idempotent for the same domain and server name.
        Wincred.WriteDomainCredentials(RdpDomain, fqdn, credentials.Username, credentials.Password);
        logger.LogDebug("wrote domain credential for {serverName} with username {username}", fqdn,
            credentials.Username);
        return Task.CompletedTask;
    }

    public Task Connect(string fqdn, int port = IRdpConnector.DefaultPort, CancellationToken ct = default)
    {
        // use mstsc to launch the RDP connection
        var mstscProc = new Process();
        mstscProc.StartInfo.FileName = "mstsc";
        var args = $"/v {fqdn}";
        if (port != IRdpConnector.DefaultPort)
        {
            args = $"/v {fqdn}:{port}";
        }

        mstscProc.StartInfo.Arguments = args;
        mstscProc.StartInfo.CreateNoWindow = true;
        mstscProc.StartInfo.UseShellExecute = false;
        try
        {
            if (!mstscProc.Start())
                throw new InvalidOperationException("Failed to start mstsc, Start returned false");
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "mstsc failed to start");

            try
            {
                mstscProc.Kill();
            }
            catch
            {
                // ignored, the process likely doesn't exist
            }

            mstscProc.Dispose();
            throw;
        }

        return mstscProc.WaitForExitAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

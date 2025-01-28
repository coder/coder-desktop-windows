using System.Runtime.InteropServices;
using Coder.Desktop.Vpn.Proto;
using CoderSdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Semver;

namespace Coder.Desktop.Vpn.Service;

public interface IManager : IDisposable
{
    public Task HandleClientRpcMessage(ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct = default);

    public Task StopAsync(CancellationToken ct = default);
}

/// <summary>
///     Manager provides handling for RPC requests from the client and from the tunnel.
/// </summary>
public class Manager : IManager
{
    // TODO: determine a suitable value for this
    private static readonly SemVersionRange ServerVersionRange = SemVersionRange.All;

    private readonly ManagerConfig _config;
    private readonly IDownloader _downloader;
    private readonly ILogger<Manager> _logger;
    private readonly ITunnelSupervisor _tunnelSupervisor;
    private SemVersion? _lastServerVersion;
    private StartRequest? _lastStartRequest;

    // ReSharper disable once ConvertToPrimaryConstructor
    public Manager(IOptions<ManagerConfig> config, ILogger<Manager> logger, IDownloader downloader,
        ITunnelSupervisor tunnelSupervisor)
    {
        _config = config.Value;
        _logger = logger;
        _downloader = downloader;
        _tunnelSupervisor = tunnelSupervisor;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Processes a message sent from a Client to the ManagerRpcService over the codervpn RPC protocol.
    /// </summary>
    /// <param name="message">Client message</param>
    /// <param name="ct">Cancellation token</param>
    public async Task HandleClientRpcMessage(ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ClientMessage: {MessageType}", message.Message.MsgCase);
        switch (message.Message.MsgCase)
        {
            case ClientMessage.MsgOneofCase.Start:
                // TODO: these sub-methods should be managed by some Task list and cancelled/awaited on stop
                await HandleClientMessageStart(message, ct);
                break;
            case ClientMessage.MsgOneofCase.Stop:
                await HandleClientMessageStop(message, ct);
                break;
            case ClientMessage.MsgOneofCase.None:
            default:
                _logger.LogWarning("Received unknown message type {MessageType}", message.Message.MsgCase);
                break;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _tunnelSupervisor.StopAsync(ct);
    }

    private async Task HandleClientMessageStart(ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct)
    {
        try
        {
            var serverVersion =
                await CheckServerVersionAndCredentials(message.Message.Start.CoderUrl, message.Message.Start.ApiToken,
                    ct);
            if (_tunnelSupervisor.IsRunning && _lastStartRequest != null &&
                _lastStartRequest.Equals(message.Message.Start) && _lastServerVersion == serverVersion)
            {
                // The client is requesting to start an identical tunnel while
                // we're already running it.
                _logger.LogInformation("ClientMessage.Start: Ignoring duplicate start request");
                await message.SendReply(new ServiceMessage
                {
                    Start = new StartResponse
                    {
                        Success = true,
                    },
                }, ct);
                return;
            }

            _lastStartRequest = message.Message.Start;
            _lastServerVersion = serverVersion;

            // Stop the tunnel if it's running so we don't have to worry about
            // permissions issues when replacing the binary.
            await _tunnelSupervisor.StopAsync(ct);
            await DownloadTunnelBinaryAsync(message.Message.Start.CoderUrl, serverVersion, ct);
            await _tunnelSupervisor.StartAsync(_config.TunnelBinaryPath, HandleTunnelRpcMessage, HandleTunnelRpcError,
                ct);

            var reply = await _tunnelSupervisor.SendRequestAwaitReply(new ManagerMessage
            {
                Start = message.Message.Start,
            }, ct);
            if (reply.MsgCase != TunnelMessage.MsgOneofCase.Start)
                throw new InvalidOperationException("Tunnel did not reply with a Start response");

            await message.SendReply(new ServiceMessage
            {
                Start = reply.Start,
            }, ct);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "ClientMessage.Start: Failed to start VPN client");
            await message.SendReply(new ServiceMessage
            {
                Start = new StartResponse
                {
                    Success = false,
                    ErrorMessage = e.ToString(),
                },
            }, ct);
        }
    }

    private async Task HandleClientMessageStop(ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct)
    {
        try
        {
            // This will handle sending the Stop message to the tunnel for us.
            await _tunnelSupervisor.StopAsync(ct);
            await message.SendReply(new ServiceMessage
            {
                Stop = new StopResponse
                {
                    Success = true,
                },
            }, ct);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "ClientMessage.Stop: Failed to stop VPN client");
            await message.SendReply(new ServiceMessage
            {
                Stop = new StopResponse
                {
                    Success = false,
                    ErrorMessage = e.Message,
                },
            }, ct);
        }
    }

    private void HandleTunnelRpcMessage(ReplyableRpcMessage<ManagerMessage, TunnelMessage> message)
    {
        // TODO: this
    }

    private void HandleTunnelRpcError(Exception e)
    {
        _logger.LogError(e, "Manager<->Tunnel RPC error");
        try
        {
            _tunnelSupervisor.StopAsync();
            // TODO: this should broadcast an update to all clients
        }
        catch (Exception e2)
        {
            _logger.LogError(e2, "Failed to stop tunnel supervisor after RPC error");
        }
    }

    /// <summary>
    ///     Returns the architecture of the current system.
    /// </summary>
    /// <returns>A golang architecture string for the binary</returns>
    /// <exception cref="PlatformNotSupportedException">Unsupported architecture</exception>
    private static string SystemArchitecture()
    {
        // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            // We only support amd64 and arm64 on Windows currently.
            _ => throw new PlatformNotSupportedException(
                $"Unsupported architecture '{RuntimeInformation.ProcessArchitecture}'. Coder only supports amd64 and arm64."),
        };
    }

    /// <summary>
    ///     Connects to the Coder server to ensure the server version is within the required range and the credentials
    ///     are valid.
    /// </summary>
    /// <param name="baseUrl">Coder server base URL</param>
    /// <param name="apiToken">Coder API token</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The server version</returns>
    /// <exception cref="InvalidOperationException">The server version is not within the required range</exception>
    private async ValueTask<SemVersion> CheckServerVersionAndCredentials(string baseUrl, string apiToken,
        CancellationToken ct = default)
    {
        var client = new CoderApiClient(baseUrl, apiToken);

        var buildInfo = await client.GetBuildInfo(ct);
        _logger.LogInformation("Fetched server version '{ServerVersion}'", buildInfo.Version);
        if (buildInfo.Version.StartsWith('v')) buildInfo.Version = buildInfo.Version[1..];
        var serverVersion = SemVersion.Parse(buildInfo.Version);
        if (!serverVersion.Satisfies(ServerVersionRange))
            throw new InvalidOperationException(
                $"Server version '{serverVersion}' is not within required server version range '{ServerVersionRange}'");

        var user = await client.GetUser(User.Me, ct);
        _logger.LogInformation("Authenticated to server as '{Username}'", user.Username);

        return serverVersion;
    }

    /// <summary>
    ///     Fetches the "/bin/coder-windows-{architecture}.exe" binary from the given base URL and writes it to the
    ///     destination path after validating the signature and checksum.
    /// </summary>
    /// <param name="baseUrl">Server base URL to download the binary from</param>
    /// <param name="expectedVersion">The version of the server to expect in the binary</param>
    /// <param name="ct">Cancellation token</param>
    /// <exception cref="ArgumentException">If the base URL is invalid</exception>
    private async Task DownloadTunnelBinaryAsync(string baseUrl, SemVersion expectedVersion,
        CancellationToken ct = default)
    {
        var architecture = SystemArchitecture();
        Uri url;
        try
        {
            url = new Uri(baseUrl, UriKind.Absolute);
            if (url.PathAndQuery != "/")
                throw new ArgumentException("Base URL must not contain a path", nameof(baseUrl));
            url = new Uri(url, $"/bin/coder-windows-{architecture}.exe");
        }
        catch (Exception e)
        {
            throw new ArgumentException($"Invalid base URL '{baseUrl}'", e);
        }

        _logger.LogInformation("Downloading VPN binary from '{url}' to '{DestinationPath}'", url,
            _config.TunnelBinaryPath);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var validators = new CombinationDownloadValidator(
            // TODO: re-enable when the binaries are signed and have versions
            //AuthenticodeDownloadValidator.Coder,
            //new AssemblyVersionDownloadValidator(
            //$"{expectedVersion.Major}.{expectedVersion.Minor}.{expectedVersion.Patch}.0")
        );
        var downloadTask = await _downloader.StartDownloadAsync(req, _config.TunnelBinaryPath, validators, ct);

        // TODO: monitor and report progress when we have a mechanism to do so

        // Awaiting this will check the checksum (via the ETag) if the file
        // exists, and will also validate the signature and version.
        await downloadTask.Task;
    }
}

using System.Runtime.InteropServices;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Semver;

namespace Coder.Desktop.Vpn.Service;

public enum TunnelStatus
{
    Starting,
    Started,
    Stopping,
    Stopped,
}

public interface IManager : IDisposable
{
    public Task StopAsync(CancellationToken ct = default);
}

/// <summary>
///     Manager provides handling for RPC requests from the client and from the tunnel.
/// </summary>
public class Manager : IManager
{
    private readonly ManagerConfig _config;
    private readonly IDownloader _downloader;
    private readonly ILogger<Manager> _logger;
    private readonly ITunnelSupervisor _tunnelSupervisor;
    private readonly IManagerRpc _managerRpc;

    private volatile TunnelStatus _status = TunnelStatus.Stopped;

    // TunnelSupervisor already has protections against concurrent operations,
    // but all the other stuff before starting the tunnel does not.
    private readonly RaiiSemaphoreSlim _tunnelOperationLock = new(1, 1);
    private ServerVersion? _lastServerVersion;
    private StartRequest? _lastStartRequest;

    private readonly RaiiSemaphoreSlim _statusLock = new(1, 1);
    private readonly List<Workspace> _trackedWorkspaces = [];
    private readonly List<Agent> _trackedAgents = [];

    // ReSharper disable once ConvertToPrimaryConstructor
    public Manager(IOptions<ManagerConfig> config, ILogger<Manager> logger, IDownloader downloader,
        ITunnelSupervisor tunnelSupervisor, IManagerRpc managerRpc)
    {
        _config = config.Value;
        _logger = logger;
        _downloader = downloader;
        _tunnelSupervisor = tunnelSupervisor;
        _managerRpc = managerRpc;
        _managerRpc.OnReceive += HandleClientRpcMessage;
    }

    public void Dispose()
    {
        _managerRpc.OnReceive -= HandleClientRpcMessage;
        GC.SuppressFinalize(this);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _tunnelSupervisor.StopAsync(ct);
        await BroadcastStatus(null, ct);
    }

    /// <summary>
    ///     Processes a message sent from a Client to the ManagerRpcService over the codervpn RPC protocol.
    /// </summary>
    /// <param name="message">Client message</param>
    /// <param name="ct">Cancellation token</param>
    public async Task HandleClientRpcMessage(ulong clientId, ReplyableRpcMessage<ServiceMessage, ClientMessage> message,
        CancellationToken ct = default)
    {
        using (_logger.BeginScope("ClientMessage.{MessageType} (client: {ClientId})", message.Message.MsgCase,
                   clientId))
        {
            switch (message.Message.MsgCase)
            {
                case ClientMessage.MsgOneofCase.Start:
                    // TODO: these sub-methods should be managed by some Task list and cancelled/awaited on stop
                    var startResponse = await HandleClientMessageStart(message.Message, ct);
                    await message.SendReply(new ServiceMessage
                    {
                        Start = startResponse,
                    }, ct);
                    break;
                case ClientMessage.MsgOneofCase.Stop:
                    var stopResponse = await HandleClientMessageStop(message.Message, ct);
                    await message.SendReply(new ServiceMessage
                    {
                        Stop = stopResponse,
                    }, ct);
                    await BroadcastStatus(null, ct);
                    break;
                case ClientMessage.MsgOneofCase.Status:
                    await message.SendReply(new ServiceMessage
                    {
                        Status = await CurrentStatus(ct),
                    }, ct);
                    break;
                case ClientMessage.MsgOneofCase.None:
                default:
                    _logger.LogWarning("Received unknown message type {MessageType}", message.Message.MsgCase);
                    break;
            }
        }
    }

    private async ValueTask<StartResponse> HandleClientMessageStart(ClientMessage message,
        CancellationToken ct)
    {
        var opLock = await _tunnelOperationLock.LockAsync(TimeSpan.FromMilliseconds(500), ct);
        if (opLock == null)
        {
            _logger.LogWarning("Tunnel operation lock timed out");
            return new StartResponse
            {
                Success = false,
                ErrorMessage = "Could not acquire tunnel operation lock, another operation is in progress",
            };
        }

        using (opLock)
        {
            try
            {
                var serverVersion =
                    await CheckServerVersionAndCredentials(message.Start.CoderUrl, message.Start.ApiToken, ct);
                if (_status == TunnelStatus.Started && _lastStartRequest != null &&
                    _lastStartRequest.Equals(message.Start) && _lastServerVersion?.RawString == serverVersion.RawString)
                {
                    // The client is requesting to start an identical tunnel while
                    // we're already running it.
                    _logger.LogInformation("Ignoring duplicate start request");
                    return new StartResponse
                    {
                        Success = true,
                    };
                }

                ClearPeers();
                await BroadcastStatus(TunnelStatus.Starting, ct);
                _lastStartRequest = message.Start;
                _lastServerVersion = serverVersion;

                // TODO: each section of this operation needs a timeout
                // Stop the tunnel if it's running so we don't have to worry about
                // permissions issues when replacing the binary.
                await _tunnelSupervisor.StopAsync(ct);
                await DownloadTunnelBinaryAsync(message.Start.CoderUrl, serverVersion.SemVersion, ct);
                await _tunnelSupervisor.StartAsync(_config.TunnelBinaryPath, HandleTunnelRpcMessage,
                    HandleTunnelRpcError,
                    ct);

                var reply = await _tunnelSupervisor.SendRequestAwaitReply(new ManagerMessage
                {
                    Start = message.Start,
                }, ct);
                if (reply.MsgCase != TunnelMessage.MsgOneofCase.Start)
                    throw new InvalidOperationException("Tunnel did not reply with a Start response");

                await BroadcastStatus(reply.Start.Success ? TunnelStatus.Started : TunnelStatus.Stopped, ct);
                return reply.Start;
            }
            catch (Exception e)
            {
                await BroadcastStatus(TunnelStatus.Stopped, ct);
                _logger.LogWarning(e, "Failed to start VPN client");
                return new StartResponse
                {
                    Success = false,
                    ErrorMessage = e.ToString(),
                };
            }
        }
    }

    private async ValueTask<StopResponse> HandleClientMessageStop(ClientMessage message,
        CancellationToken ct)
    {
        var opLock = await _tunnelOperationLock.LockAsync(TimeSpan.FromMilliseconds(500), ct);
        if (opLock == null)
        {
            _logger.LogWarning("Tunnel operation lock timed out");
            return new StopResponse
            {
                Success = false,
                ErrorMessage = "Could not acquire tunnel operation lock, another operation is in progress",
            };
        }

        using (opLock)
        {
            try
            {
                ClearPeers();
                await BroadcastStatus(TunnelStatus.Stopping, ct);
                // This will handle sending the Stop message to the tunnel for us.
                await _tunnelSupervisor.StopAsync(ct);
                return new StopResponse
                {
                    Success = true,
                };
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to stop VPN client");
                return new StopResponse
                {
                    Success = false,
                    ErrorMessage = e.ToString(),
                };
            }
            finally
            {
                // Always assume it's stopped.
                await BroadcastStatus(TunnelStatus.Stopped, ct);
            }
        }
    }

    private void HandleTunnelRpcMessage(ReplyableRpcMessage<ManagerMessage, TunnelMessage> message)
    {
        using (_logger.BeginScope("TunnelMessage.{MessageType}", message.Message.MsgCase))
        {
            switch (message.Message.MsgCase)
            {
                case TunnelMessage.MsgOneofCase.Start:
                case TunnelMessage.MsgOneofCase.Stop:
                    _logger.LogWarning("Received unexpected message reply type {MessageType}", message.Message.MsgCase);
                    break;
                case TunnelMessage.MsgOneofCase.Log:
                case TunnelMessage.MsgOneofCase.NetworkSettings:
                    _logger.LogWarning("Received message type {MessageType} that is not expected on Windows",
                        message.Message.MsgCase);
                    break;
                case TunnelMessage.MsgOneofCase.PeerUpdate:
                    HandleTunnelMessagePeerUpdate(message.Message);
                    BroadcastStatus().Wait();
                    break;
                case TunnelMessage.MsgOneofCase.None:
                default:
                    _logger.LogWarning("Received unknown message type {MessageType}", message.Message.MsgCase);
                    break;
            }
        }
    }

    private void ClearPeers()
    {
        using var _ = _statusLock.Lock();
        _trackedWorkspaces.Clear();
        _trackedAgents.Clear();
    }

    private void HandleTunnelMessagePeerUpdate(TunnelMessage message)
    {
        using var _ = _statusLock.Lock();
        foreach (var newWorkspace in message.PeerUpdate.UpsertedWorkspaces)
        {
            _trackedWorkspaces.RemoveAll(w => w.Id == newWorkspace.Id);
            _trackedWorkspaces.Add(newWorkspace);
        }

        foreach (var removedWorkspace in message.PeerUpdate.DeletedWorkspaces)
            _trackedWorkspaces.RemoveAll(w => w.Id == removedWorkspace.Id);
        foreach (var newAgent in message.PeerUpdate.UpsertedAgents)
        {
            _trackedAgents.RemoveAll(a => a.Id == newAgent.Id);
            _trackedAgents.Add(newAgent);
        }

        foreach (var removedAgent in message.PeerUpdate.DeletedAgents)
            _trackedAgents.RemoveAll(a => a.Id == removedAgent.Id);

        _trackedWorkspaces.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        _trackedAgents.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    private async ValueTask<Status> CurrentStatus(CancellationToken ct = default)
    {
        using var _ = await _statusLock.LockAsync(ct);
        var lifecycle = _status switch
        {
            TunnelStatus.Starting => Status.Types.Lifecycle.Starting,
            TunnelStatus.Started => Status.Types.Lifecycle.Started,
            TunnelStatus.Stopping => Status.Types.Lifecycle.Stopping,
            TunnelStatus.Stopped => Status.Types.Lifecycle.Stopped,
            _ => Status.Types.Lifecycle.Stopped,
        };

        return new Status
        {
            Lifecycle = lifecycle,
            ErrorMessage = "",
            PeerUpdate = new PeerUpdate
            {
                UpsertedAgents = { _trackedAgents },
                UpsertedWorkspaces = { _trackedWorkspaces },
            },
        };
    }

    private async Task BroadcastStatus(TunnelStatus? newStatus = null, CancellationToken ct = default)
    {
        if (newStatus != null) _status = newStatus.Value;
        await _managerRpc.BroadcastAsync(new ServiceMessage
        {
            Status = await CurrentStatus(ct),
        }, ct);
    }

    private void HandleTunnelRpcError(Exception e)
    {
        _logger.LogError(e, "Manager<->Tunnel RPC error");
        try
        {
            _tunnelSupervisor.StopAsync();
            ClearPeers();
            BroadcastStatus().Wait();
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
    private async ValueTask<ServerVersion> CheckServerVersionAndCredentials(string baseUrl, string apiToken,
        CancellationToken ct = default)
    {
        var client = new CoderApiClient(baseUrl, apiToken);

        var buildInfo = await client.GetBuildInfo(ct);
        _logger.LogInformation("Fetched server version '{ServerVersion}'", buildInfo.Version);
        var serverVersion = ServerVersionUtilities.ParseAndValidateServerVersion(buildInfo.Version);
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

        var validators = new CombinationDownloadValidator();
        if (!string.IsNullOrEmpty(_config.TunnelBinarySignatureSigner))
        {
            _logger.LogDebug("Adding Authenticode signature validator for signer '{Signer}'",
                _config.TunnelBinarySignatureSigner);
            validators.Add(new AuthenticodeDownloadValidator(_config.TunnelBinarySignatureSigner));
        }
        else
        {
            _logger.LogDebug("Skipping Authenticode signature validation");
        }

        if (!_config.TunnelBinaryAllowVersionMismatch)
        {
            _logger.LogDebug("Adding version validator for version '{ExpectedVersion}'", expectedVersion);
            validators.Add(new AssemblyVersionDownloadValidator((int)expectedVersion.Major, (int)expectedVersion.Minor,
                (int)expectedVersion.Patch));
        }
        else
        {
            _logger.LogDebug("Skipping tunnel binary version validation");
        }

        var downloadTask = await _downloader.StartDownloadAsync(req, _config.TunnelBinaryPath, validators, ct);

        // TODO: monitor and report progress when we have a mechanism to do so

        // Awaiting this will check the checksum (via the ETag) if the file
        // exists, and will also validate the signature and version.
        await downloadTask.Task;
    }
}

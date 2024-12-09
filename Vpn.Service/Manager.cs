using System.Runtime.InteropServices;
using Coder.Desktop.Vpn.Proto;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.Vpn.Service;

public interface IManager
{
    public Task HandleClientRpcMessage(ReplyableRpcMessage<ManagerMessage, ManagerMessage> message,
        CancellationToken ct = default);

    public Task StopAsync(CancellationToken ct = default);
}

public class Manager : IManager, IAsyncDisposable
{
    private const string DestinationPath = "C:\\coder-vpn.exe";
    private readonly IDownloader _downloader;

    private readonly ILogger<Manager> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public Manager(ILogger<Manager> logger, IDownloader downloader)
    {
        _logger = logger;
        _downloader = downloader;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }

    public async Task HandleClientRpcMessage(ReplyableRpcMessage<ManagerMessage, ManagerMessage> message,
        CancellationToken ct = default)
    {
        switch (message.Message.MsgCase)
        {
            default:
                _logger.LogWarning("Received unknown message type {MessageType}", message.Message.MsgCase);
                break;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        // TODO: implement once we have process supervision
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Returns the architecture of the current system.
    /// </summary>
    /// <returns>A golang architecture string for the binary</returns>
    /// <exception cref="PlatformNotSupportedException">Unsupported architecture</exception>
    private static string SystemArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            // We only support amd64 and arm64 on Windows currently.
            _ => throw new PlatformNotSupportedException(
                "Unsupported architecture. Coder only supports amd64 and arm64."),
        };
    }

    /// <summary>
    ///     Fetches the "/bin/coder-windows-{architecture}.exe" binary from the given base URL and writes it to the
    ///     destination path after validating the signature and checksum.
    /// </summary>
    /// <param name="baseUrl"></param>
    /// <param name="ct"></param>
    /// <exception cref="ArgumentException"></exception>
    private async Task DownloadVPNClientAsync(string baseUrl, CancellationToken ct = default)
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

        _logger.LogInformation("Downloading VPN binary from '{url}' to '{DestinationPath}'", url, DestinationPath);
        var downloadTask =
            await _downloader.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), DestinationPath,
                AuthenticodeDownloadValidator.Coder, ct);

        // TODO: monitor and report progress when we have a mechanism to do so

        // Awaiting this will check the checksum (via the ETag) if provided,
        // and will also validate the signature using the validator we supplied.
        await downloadTask.Task;
    }
}

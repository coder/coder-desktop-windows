using System.Diagnostics;
using System.IO.Pipes;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Utilities;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.Vpn.Service;

public interface ITunnelSupervisor : IAsyncDisposable
{
    /// <summary>
    ///     Starts the tunnel subprocess with the given executable path. If the subprocess is already running, this method will
    ///     kill it first.
    /// </summary>
    /// <param name="binPath">Path to the executable</param>
    /// <param name="messageHandler">Handler to call with each RPC message</param>
    /// <param name="errorHandler">
    ///     Handler for permanent errors from the RPC Speaker. The recipient should call StopAsync after
    ///     receiving this.
    /// </param>
    /// <param name="ct">Cancellation token</param>
    public Task StartAsync(string binPath,
        Speaker<ManagerMessage, TunnelMessage>.OnReceiveDelegate messageHandler,
        Speaker<ManagerMessage, TunnelMessage>.OnErrorDelegate errorHandler,
        CancellationToken ct = default);

    /// <summary>
    ///     Stops the tunnel subprocess. If the subprocess is not running, this method does nothing.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public Task StopAsync(CancellationToken ct = default);
}

/// <summary>
///     Launches and supervises the tunnel subprocess. Provides RPC communication with the subprocess.
/// </summary>
public class TunnelSupervisor : ITunnelSupervisor
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<TunnelSupervisor> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private AnonymousPipeServerStream? _inPipe;
    private AnonymousPipeServerStream? _outPipe;
    private Speaker<ManagerMessage, TunnelMessage>? _speaker;

    private Process? _subprocess;

    // ReSharper disable once ConvertToPrimaryConstructor
    public TunnelSupervisor(ILogger<TunnelSupervisor> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(string binPath,
        Speaker<ManagerMessage, TunnelMessage>.OnReceiveDelegate messageHandler,
        Speaker<ManagerMessage, TunnelMessage>.OnErrorDelegate errorHandler,
        CancellationToken ct = default)
    {
        _logger.LogInformation("StartAsync(\"{binPath}\")", binPath);
        if (!await _operationLock.WaitAsync(0, ct))
            throw new InvalidOperationException(
                "Another TunnelSupervisor Start or Stop operation is already in progress");

        try
        {
            await CleanupAsync(ct);

            _outPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            _inPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            _subprocess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binPath,
                    ArgumentList = { "vpn-daemon", "run" },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            // Pass the other end of the pipes to the subprocess and dispose
            // the local copies.
            _subprocess.StartInfo.Environment.Add("CODER_VPN_DAEMON_RPC_READ_HANDLE",
                _outPipe.GetClientHandleAsString());
            _subprocess.StartInfo.Environment.Add("CODER_VPN_DAEMON_RPC_WRITE_HANDLE",
                _inPipe.GetClientHandleAsString());
            _outPipe.DisposeLocalCopyOfClientHandle();
            _inPipe.DisposeLocalCopyOfClientHandle();

            _logger.LogInformation("StartAsync: starting subprocess");
            _subprocess.Start();
            _logger.LogInformation("StartAsync: subprocess started");

            // We don't use the supplied CancellationToken here because we want it to only apply to the startup
            // procedure.
            _ = _subprocess.WaitForExitAsync(_cts.Token).ContinueWith(OnProcessExited, CancellationToken.None);

            // Start the RPC Speaker.
            try
            {
                var stream = new BidirectionalPipe(_inPipe, _outPipe);
                _speaker = new Speaker<ManagerMessage, TunnelMessage>(stream);
                _speaker.Receive += messageHandler;
                _speaker.Error += errorHandler;
                // Handshakes already have a 5-second timeout.
                await _speaker.StartAsync(ct);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to start RPC Speaker on pipes to subprocess", e);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "StartAsync: failed to start or connect to subprocess");
            await CleanupAsync(ct);
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StopAsync()");
        if (!await _operationLock.WaitAsync(0, ct))
            throw new InvalidOperationException(
                "Another TunnelSupervisor Start or Stop operation is already in progress");

        try
        {
            await CleanupAsync(ct);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Dispose();
        await CleanupAsync();
        GC.SuppressFinalize(this);
    }

    private async Task OnProcessExited(Task task)
    {
        if (task.IsFaulted)
        {
            _logger.LogError(task.Exception, "OnProcessExited: subprocess exited with an exception");
            return;
        }

        if (!await _operationLock.WaitAsync(0)) _logger.LogInformation("OnProcessExited: subprocess exited");

        try
        {
            await CleanupAsync();
            _logger.LogInformation("OnProcessExited: subprocess exited with code {ExitCode}",
                _subprocess?.ExitCode ?? -1);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Cleans up the pipes and the subprocess if it's still running. This method should not be called without holding the
    ///     semaphore.
    /// </summary>
    private async Task CleanupAsync(CancellationToken ct = default)
    {
        if (_speaker != null)
        {
            try
            {
                _logger.LogInformation("CleanupAsync: Sending stop message to subprocess");
                var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stopCts.CancelAfter(5000);
                await _speaker.SendRequestAwaitReply(new ManagerMessage
                {
                    Stop = new StopRequest(),
                }, stopCts.Token);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CleanupAsync: Failed to send stop message to subprocess");
            }

            try
            {
                _logger.LogInformation("CleanupAsync: Disposing _speaker");
                await _speaker.DisposeAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CleanupAsync: Failed to stop/dispose _speaker");
            }
            finally
            {
                _speaker = null;
            }
        }

        if (_outPipe != null)
        {
            _logger.LogInformation("CleanupAsync: Disposing _outPipe");
            try
            {
                await _outPipe.DisposeAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CleanupAsync: Failed to dispose _outPipe");
            }
            finally
            {
                _outPipe = null;
            }
        }

        if (_inPipe != null)
        {
            _logger.LogInformation("CleanupAsync: Disposing _inPipe");
            try
            {
                await _inPipe.DisposeAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CleanupAsync: Failed to dispose _inPipe");
            }
            finally
            {
                _inPipe = null;
            }
        }

        if (_subprocess != null)
            try
            {
                if (!_subprocess.HasExited)
                {
                    // TODO: is there a nicer way we can do this?
                    _logger.LogInformation("CleanupAsync: Killing un-exited _subprocess");
                    _subprocess.Kill();
                    // Since we just killed the process ideally it should exit
                    // immediately.
                    var exitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    exitCts.CancelAfter(5000);
                    await _subprocess.WaitForExitAsync(exitCts.Token);
                }

                _logger.LogInformation("CleanupAsync: Disposing _subprocess");
                _subprocess.Dispose();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CleanupAsync: Failed to kill/dispose _subprocess");
            }
            finally
            {
                _subprocess = null;
            }
    }
}

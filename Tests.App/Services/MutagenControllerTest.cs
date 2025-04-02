using System.Diagnostics;
using System.Runtime.InteropServices;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using NUnit.Framework.Interfaces;

namespace Coder.Desktop.Tests.App.Services;

[TestFixture]
public class MutagenControllerTest
{
    [OneTimeSetUp]
    public async Task DownloadMutagen()
    {
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
        var scriptDirectory = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "scripts"));
        var process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.Arguments = $"-ExecutionPolicy Bypass -File Get-Mutagen.ps1 -arch {_arch}";
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.WorkingDirectory = scriptDirectory;
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        TestContext.Out.Write(output);
        var error = await process.StandardError.ReadToEndAsync(ct);
        TestContext.Error.Write(error);
        Assert.That(process.ExitCode, Is.EqualTo(0));
        _mutagenBinaryPath = Path.Combine(scriptDirectory, "files", $"mutagen-windows-{_arch}.exe");
        Assert.That(File.Exists(_mutagenBinaryPath));
    }

    [SetUp]
    public void CreateTempDir()
    {
        _tempDirectory = Directory.CreateTempSubdirectory(GetType().Name);
        TestContext.Out.WriteLine($"temp directory: {_tempDirectory}");
    }

    [TearDown]
    public void DeleteTempDir()
    {
        // Only delete the temp directory if the test passed.
        if (TestContext.CurrentContext.Result.Outcome == ResultState.Success)
            _tempDirectory.Delete(true);
        else
            TestContext.Out.WriteLine($"persisting temp directory: {_tempDirectory}");
    }

    private string _mutagenBinaryPath;
    private DirectoryInfo _tempDirectory;

    private readonly string _arch = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        // We only support amd64 and arm64 on Windows currently.
        _ => throw new PlatformNotSupportedException(
            $"Unsupported architecture '{RuntimeInformation.ProcessArchitecture}'. Coder only supports x64 and arm64."),
    };

    /// <summary>
    ///     Ensures the daemon is stopped by waiting for the daemon.lock file to be released.
    /// </summary>
    private static async Task AssertDaemonStopped(string dataDirectory, CancellationToken ct)
    {
        var lockPath = Path.Combine(dataDirectory, "daemon", "daemon.lock");
        // If we can lock the daemon.lock file, it means the daemon has stopped.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var lockFile = new FileStream(lockPath, FileMode.Open, FileAccess.Write, FileShare.None);
            }
            catch (IOException e)
            {
                TestContext.Out.WriteLine($"Could not acquire daemon.lock (will retry): {e.Message}");
                await Task.Delay(100, ct);
            }

            break;
        }
    }

    [Test(Description = "Full sync test")]
    [CancelAfter(30_000)]
    public async Task Ok(CancellationToken ct)
    {
        // NUnit runs each test in a temporary directory
        var dataDirectory = _tempDirectory.CreateSubdirectory("mutagen").FullName;
        var alphaDirectory = _tempDirectory.CreateSubdirectory("alpha");
        var betaDirectory = _tempDirectory.CreateSubdirectory("beta");

        await using var controller = new MutagenController(_mutagenBinaryPath, dataDirectory);

        // Initial state before calling RefreshState.
        var state = controller.GetState();
        Assert.That(state.Lifecycle, Is.EqualTo(SyncSessionControllerLifecycle.Uninitialized));
        Assert.That(state.DaemonError, Is.Null);
        Assert.That(state.DaemonLogFilePath, Is.EqualTo(Path.Combine(dataDirectory, "daemon.log")));
        Assert.That(state.SyncSessions, Is.Empty);

        state = await controller.RefreshState(ct);
        Assert.That(state.Lifecycle, Is.EqualTo(SyncSessionControllerLifecycle.Stopped));
        Assert.That(state.DaemonError, Is.Null);
        Assert.That(state.DaemonLogFilePath, Is.EqualTo(Path.Combine(dataDirectory, "daemon.log")));
        Assert.That(state.SyncSessions, Is.Empty);

        // Ensure the daemon is stopped because all sessions are terminated.
        await AssertDaemonStopped(dataDirectory, ct);

        var session1 = await controller.CreateSyncSession(new CreateSyncSessionRequest
        {
            Alpha = new CreateSyncSessionRequestEndpoint
            {
                Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                Path = alphaDirectory.FullName,
            },
            Beta = new CreateSyncSessionRequestEndpoint
            {
                Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                Path = betaDirectory.FullName,
            },
        }, ct);

        state = controller.GetState();
        Assert.That(state.SyncSessions, Has.Count.EqualTo(1));
        Assert.That(state.SyncSessions[0].Identifier, Is.EqualTo(session1.Identifier));

        var session2 = await controller.CreateSyncSession(new CreateSyncSessionRequest
        {
            Alpha = new CreateSyncSessionRequestEndpoint
            {
                Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                Path = alphaDirectory.FullName,
            },
            Beta = new CreateSyncSessionRequestEndpoint
            {
                Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                Path = betaDirectory.FullName,
            },
        }, ct);

        state = controller.GetState();
        Assert.That(state.SyncSessions, Has.Count.EqualTo(2));
        Assert.That(state.SyncSessions.Any(s => s.Identifier == session1.Identifier));
        Assert.That(state.SyncSessions.Any(s => s.Identifier == session2.Identifier));

        // Write a file to alpha.
        var alphaFile = Path.Combine(alphaDirectory.FullName, "file.txt");
        var betaFile = Path.Combine(betaDirectory.FullName, "file.txt");
        const string alphaContent = "hello";
        await File.WriteAllTextAsync(alphaFile, alphaContent, ct);

        // Wait for the file to appear in beta.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(100, ct);
            if (!File.Exists(betaFile))
            {
                TestContext.Out.WriteLine("Waiting for file to appear in beta");
                continue;
            }

            var betaContent = await File.ReadAllTextAsync(betaFile, ct);
            if (betaContent == alphaContent) break;
            TestContext.Out.WriteLine($"Waiting for file contents to match, current: {betaContent}");
        }

        await controller.TerminateSyncSession(session1.Identifier, ct);
        await controller.TerminateSyncSession(session2.Identifier, ct);

        // Ensure the daemon is stopped because all sessions are terminated.
        await AssertDaemonStopped(dataDirectory, ct);

        state = controller.GetState();
        Assert.That(state.Lifecycle, Is.EqualTo(SyncSessionControllerLifecycle.Stopped));
        Assert.That(state.DaemonError, Is.Null);
        Assert.That(state.DaemonLogFilePath, Is.EqualTo(Path.Combine(dataDirectory, "daemon.log")));
        Assert.That(state.SyncSessions, Is.Empty);
    }

    [Test(Description = "Shut down daemon when no sessions")]
    [CancelAfter(30_000)]
    public async Task ShutdownNoSessions(CancellationToken ct)
    {
        // NUnit runs each test in a temporary directory
        var dataDirectory = _tempDirectory.FullName;
        await using var controller = new MutagenController(_mutagenBinaryPath, dataDirectory);
        await controller.RefreshState(ct);

        // log file tells us the daemon was started.
        var logPath = Path.Combine(dataDirectory, "daemon.log");
        Assert.That(File.Exists(logPath));

        // Ensure the daemon is stopped.
        await AssertDaemonStopped(dataDirectory, ct);
    }

    [Test(Description = "Daemon is restarted when we create a session")]
    [CancelAfter(30_000)]
    public async Task CreateRestartsDaemon(CancellationToken ct)
    {
        // NUnit runs each test in a temporary directory
        var dataDirectory = _tempDirectory.CreateSubdirectory("mutagen").FullName;
        var alphaDirectory = _tempDirectory.CreateSubdirectory("alpha");
        var betaDirectory = _tempDirectory.CreateSubdirectory("beta");

        await using (var controller = new MutagenController(_mutagenBinaryPath, dataDirectory))
        {
            await controller.RefreshState(ct);
            await controller.CreateSyncSession(new CreateSyncSessionRequest
            {
                Alpha = new CreateSyncSessionRequestEndpoint
                {
                    Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                    Path = alphaDirectory.FullName,
                },
                Beta = new CreateSyncSessionRequestEndpoint
                {
                    Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                    Path = betaDirectory.FullName,
                },
            }, ct);
        }

        await AssertDaemonStopped(dataDirectory, ct);
        var logPath = Path.Combine(dataDirectory, "daemon.log");
        Assert.That(File.Exists(logPath));
        var logLines = await File.ReadAllLinesAsync(logPath, ct);

        // Here we're going to use the log to verify the daemon was started 2 times.
        // slightly brittle, but unlikely this log line will change.
        Assert.That(logLines.Count(s => s.Contains("[sync] Session manager initialized")), Is.EqualTo(2));
    }

    [Test(Description = "Controller kills orphaned daemon")]
    [CancelAfter(30_000)]
    public async Task Orphaned(CancellationToken ct)
    {
        // NUnit runs each test in a temporary directory
        var dataDirectory = _tempDirectory.CreateSubdirectory("mutagen").FullName;
        var alphaDirectory = _tempDirectory.CreateSubdirectory("alpha");
        var betaDirectory = _tempDirectory.CreateSubdirectory("beta");

        MutagenController? controller1 = null;
        MutagenController? controller2 = null;
        try
        {
            controller1 = new MutagenController(_mutagenBinaryPath, dataDirectory);
            await controller1.RefreshState(ct);
            await controller1.CreateSyncSession(new CreateSyncSessionRequest
            {
                Alpha = new CreateSyncSessionRequestEndpoint
                {
                    Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                    Path = alphaDirectory.FullName,
                },
                Beta = new CreateSyncSessionRequestEndpoint
                {
                    Protocol = CreateSyncSessionRequestEndpointProtocol.Local,
                    Path = betaDirectory.FullName,
                },
            }, ct);

            controller2 = new MutagenController(_mutagenBinaryPath, dataDirectory);
            await controller2.RefreshState(ct);
        }
        finally
        {
            if (controller1 != null) await controller1.DisposeAsync();
            if (controller2 != null) await controller2.DisposeAsync();
        }

        await AssertDaemonStopped(dataDirectory, ct);

        var logPath = Path.Combine(dataDirectory, "daemon.log");
        Assert.That(File.Exists(logPath));
        var logLines = await File.ReadAllLinesAsync(logPath, ct);

        // Here we're going to use the log to verify the daemon was started 3 times.
        // slightly brittle, but unlikely this log line will change.
        Assert.That(logLines.Count(s => s.Contains("[sync] Session manager initialized")), Is.EqualTo(3));
    }
}

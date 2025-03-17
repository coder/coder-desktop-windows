using System.Diagnostics;
using System.Runtime.InteropServices;
using Coder.Desktop.App.Services;

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

    private readonly string _arch = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        // We only support amd64 and arm64 on Windows currently.
        _ => throw new PlatformNotSupportedException(
            $"Unsupported architecture '{RuntimeInformation.ProcessArchitecture}'. Coder only supports x64 and arm64."),
    };

    private string _mutagenBinaryPath;
    private DirectoryInfo _tempDirectory;

    [Test(Description = "Shut down daemon when no sessions")]
    [CancelAfter(30_000)]
    public async Task ShutdownNoSessions(CancellationToken ct)
    {
        // NUnit runs each test in a temporary directory
        var dataDirectory = _tempDirectory.FullName;
        await using var controller = new MutagenController(_mutagenBinaryPath, dataDirectory);
        await controller.Initialize(ct);

        // log file tells us the daemon was started.
        var logPath = Path.Combine(dataDirectory, "daemon.log");
        Assert.That(File.Exists(logPath));

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
                TestContext.Out.WriteLine($"Didn't get lock (will retry): {e.Message}");
                await Task.Delay(100, ct);
            }

            break;
        }
    }

    [Test(Description = "Daemon is restarted when we create a session")]
    [CancelAfter(30_000)]
    public async Task CreateRestartsDaemon(CancellationToken ct)
    {
        // NUnit runs each test in a temporary directory
        var dataDirectory = _tempDirectory.FullName;
        await using (var controller = new MutagenController(_mutagenBinaryPath, dataDirectory))
        {
            await controller.Initialize(ct);
            await controller.CreateSyncSession(new SyncSession(), ct);
        }

        var logPath = Path.Combine(dataDirectory, "daemon.log");
        Assert.That(File.Exists(logPath));
        var logLines = File.ReadAllLines(logPath);

        // Here we're going to use the log to verify the daemon was started 2 times.
        // slightly brittle, but unlikely this log line will change.
        Assert.That(logLines.Count(s => s.Contains("[sync] Session manager initialized")), Is.EqualTo(2));
    }

    [Test(Description = "Controller kills orphaned daemon")]
    [CancelAfter(30_000)]
    public async Task Orphaned(CancellationToken ct)
    {
        // NUnit runs each test in a temporary directory
        var dataDirectory = _tempDirectory.FullName;
        MutagenController? controller1 = null;
        MutagenController? controller2 = null;
        try
        {
            controller1 = new MutagenController(_mutagenBinaryPath, dataDirectory);
            await controller1.Initialize(ct);
            await controller1.CreateSyncSession(new SyncSession(), ct);

            controller2 = new MutagenController(_mutagenBinaryPath, dataDirectory);
            await controller2.Initialize(ct);
        }
        finally
        {
            if (controller1 != null) await controller1.DisposeAsync();
            if (controller2 != null) await controller2.DisposeAsync();
        }

        var logPath = Path.Combine(dataDirectory, "daemon.log");
        Assert.That(File.Exists(logPath));
        var logLines = File.ReadAllLines(logPath);

        // Here we're going to use the log to verify the daemon was started 3 times.
        // slightly brittle, but unlikely this log line will change.
        Assert.That(logLines.Count(s => s.Contains("[sync] Session manager initialized")), Is.EqualTo(3));
    }

    // TODO: Add more tests once we actually implement creating sessions on the daemon
}

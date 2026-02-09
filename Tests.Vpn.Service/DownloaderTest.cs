using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Coder.Desktop.Vpn.Service;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;

namespace Coder.Desktop.Tests.Vpn.Service;

public class TestDownloadValidator : IDownloadValidator
{
    private readonly Exception _e;

    public TestDownloadValidator(Exception e)
    {
        _e = e;
    }

    public Task ValidateAsync(string path, CancellationToken ct = default)
    {
        throw _e;
    }
}

#if WINDOWS
[TestFixture]
[Platform("Win", Reason = "AuthenticodeDownloadValidator requires Windows Authenticode APIs")]
public class AuthenticodeDownloadValidatorTest
{
    [Test(Description = "Test an unsigned binary")]
    [CancelAfter(30_000)]
    public void Unsigned(CancellationToken ct)
    {
        var testBinaryPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "hello.exe");
        var ex = Assert.ThrowsAsync<Exception>(() =>
            AuthenticodeDownloadValidator.Coder.ValidateAsync(testBinaryPath, ct));
        Assert.That(ex.Message,
            Does.Contain(
                "File is not signed and trusted with an Authenticode signature: State=Unsigned, StateReason=None"));
    }

    [Test(Description = "Test an untrusted binary")]
    [CancelAfter(30_000)]
    public void Untrusted(CancellationToken ct)
    {
        var testBinaryPath =
            Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "hello-self-signed.exe");
        var ex = Assert.ThrowsAsync<Exception>(() =>
            AuthenticodeDownloadValidator.Coder.ValidateAsync(testBinaryPath, ct));
        Assert.That(ex.Message,
            Does.Contain(
                "File is not signed and trusted with an Authenticode signature: State=Unsigned, StateReason=UntrustedRoot"));
    }

    [Test(Description = "Test an binary with a detached signature (catalog file)")]
    [CancelAfter(30_000)]
    public void DifferentCertTrusted(CancellationToken ct)
    {
        // rundll32.exe uses a catalog file for its signature.
        var ex = Assert.ThrowsAsync<Exception>(() =>
            AuthenticodeDownloadValidator.Coder.ValidateAsync(@"C:\Windows\System32\rundll32.exe", ct));
        Assert.That(ex.Message,
            Does.Contain("File is not signed with an embedded Authenticode signature: Kind=Catalog"));
    }

    [Test(Description = "Test a binary signed by a non-EV certificate")]
    [CancelAfter(30_000)]
    public void NonEvCert(CancellationToken ct)
    {
        // dotnet.exe is signed by .NET. During tests we can be pretty sure
        // this is installed.
        var ex = Assert.ThrowsAsync<Exception>(() =>
            AuthenticodeDownloadValidator.Coder.ValidateAsync(@"C:\Program Files\dotnet\dotnet.exe", ct));
        Assert.That(ex.Message,
            Does.Contain(
                "File is not signed with an Extended Validation Code Signing certificate"));
    }

    [Test(Description = "Test a binary signed by an EV certificate with a different name")]
    [CancelAfter(30_000)]
    public void EvDifferentCertName(CancellationToken ct)
    {
        var testBinaryPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata",
            "hello-versioned-signed.exe");
        var ex = Assert.ThrowsAsync<Exception>(() =>
            new AuthenticodeDownloadValidator("Acme Corporation").ValidateAsync(testBinaryPath, ct));
        Assert.That(ex.Message,
            Does.Contain(
                "File is signed by an unexpected certificate: ExpectedName='Acme Corporation', ActualName='Coder Technologies Inc.'"));
    }

    [Test(Description = "Test a binary signed by Coder's certificate")]
    [CancelAfter(30_000)]
    public async Task CoderSigned(CancellationToken ct)
    {
        var testBinaryPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata",
            "hello-versioned-signed.exe");
        await AuthenticodeDownloadValidator.Coder.ValidateAsync(testBinaryPath, ct);
    }

    [Test(Description = "Test if the EV check works")]
    public void IsEvCert()
    {
        // To avoid potential API misuse the function is private.
        var method = typeof(AuthenticodeDownloadValidator).GetMethod("IsExtendedValidationCertificate",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "Could not find IsExtendedValidationCertificate method");

        // Call it with various certificates.
        var certs = new List<(string, bool)>
        {
            // EV:
            (Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "coder-ev.crt"), true),
            (Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "google-llc-ev.crt"), true),
            (Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "self-signed-ev.crt"), true),
            // Not EV:
            (Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "mozilla-corporation.crt"), false),
            (Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "self-signed.crt"), false),
        };

        foreach (var (certPath, isEv) in certs)
        {
            var x509Cert = new X509Certificate2(certPath);
            var result = (bool?)method!.Invoke(null, [x509Cert]);
            Assert.That(result, Is.Not.Null,
                $"IsExtendedValidationCertificate returned null for {Path.GetFileName(certPath)}");
            Assert.That(result, Is.EqualTo(isEv),
                $"IsExtendedValidationCertificate returned wrong result for {Path.GetFileName(certPath)}");
        }
    }
}
#endif

[TestFixture]
[Platform("Win", Reason = "AssemblyVersionDownloadValidator tests use Windows PE test binaries")]
public class AssemblyVersionDownloadValidatorTest
{
    [Test(Description = "No version on binary")]
    [CancelAfter(30_000)]
    public void NoVersion(CancellationToken ct)
    {
        var testBinaryPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "hello.exe");
        var ex = Assert.ThrowsAsync<Exception>(() =>
            new AssemblyVersionDownloadValidator(1, 2, 3, 4).ValidateAsync(testBinaryPath, ct));
        Assert.That(ex.Message, Does.Contain("File ProductVersion is empty or null"));
    }

    [Test(Description = "Invalid version on binary")]
    [CancelAfter(30_000)]
    public void InvalidVersion(CancellationToken ct)
    {
        var testBinaryPath =
            Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "hello-invalid-version.exe");
        var ex = Assert.ThrowsAsync<Exception>(() =>
            new AssemblyVersionDownloadValidator(1, 2, 3, 4).ValidateAsync(testBinaryPath, ct));
        Assert.That(ex.Message, Does.Contain("File ProductVersion '1-2-3-4' is not a valid version string"));
    }

    [Test(Description = "Version mismatch with full version check")]
    [CancelAfter(30_000)]
    public void VersionMismatchFull(CancellationToken ct)
    {
        var testBinaryPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata",
            "hello-versioned-signed.exe");

        // Try changing each version component one at a time
        var expectedVersions = new[] { 1, 2, 3, 4 };
        for (var i = 0; i < 4; i++)
        {
            var testVersions = (int[])expectedVersions.Clone();
            testVersions[i]++; // Increment this component to make it wrong

            var ex = Assert.ThrowsAsync<Exception>(() =>
                new AssemblyVersionDownloadValidator(
                    testVersions[0], testVersions[1], testVersions[2], testVersions[3]
                ).ValidateAsync(testBinaryPath, ct));

            Assert.That(ex.Message, Does.Contain(
                $"File ProductVersion does not match expected version: Actual='1.2.3.4', Expected='{string.Join(".", testVersions)}'"));
        }
    }

    [Test(Description = "Version match with and without partial version check")]
    [CancelAfter(30_000)]
    public async Task VersionMatch(CancellationToken ct)
    {
        var testBinaryPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata",
            "hello-versioned-signed.exe");

        // Test with just major.minor
        await new AssemblyVersionDownloadValidator(1, 2).ValidateAsync(testBinaryPath, ct);
        // Test with major.minor.patch
        await new AssemblyVersionDownloadValidator(1, 2, 3).ValidateAsync(testBinaryPath, ct);
        // Test with major.minor.patch.build
        await new AssemblyVersionDownloadValidator(1, 2, 3, 4).ValidateAsync(testBinaryPath, ct);
    }
}

[TestFixture]
public class CombinationDownloadValidatorTest
{
    [Test(Description = "All validators pass")]
    [CancelAfter(30_000)]
    public async Task AllPass(CancellationToken ct)
    {
        var validator = new CombinationDownloadValidator(
            NullDownloadValidator.Instance,
            NullDownloadValidator.Instance
        );
        await validator.ValidateAsync("test", ct);
    }

    [Test(Description = "A validator fails")]
    [CancelAfter(30_000)]
    public void Fail(CancellationToken ct)
    {
        var validator = new CombinationDownloadValidator(
            NullDownloadValidator.Instance,
            new TestDownloadValidator(new Exception("test exception"))
        );
        var ex = Assert.ThrowsAsync<Exception>(() => validator.ValidateAsync("test", ct));
        Assert.That(ex.Message, Is.EqualTo("test exception"));
    }
}

[TestFixture]
public class DownloaderTest
{
    // FYI, SetUp and TearDown get called before and after each test.
    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coder.Desktop.Tests.Vpn.Service_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_tempDir, true);
    }

    private string _tempDir;

    private static TestHttpServer EchoServer()
    {
        // Create webserver that replies to `/xyz` with a test file containing
        // `xyz`.
        return new TestHttpServer(async ctx =>
        {
            // Get the path without the leading slash.
            var path = ctx.Request.Url!.AbsolutePath[1..];
            var pathBytes = Encoding.UTF8.GetBytes(path);

            // If the client sends an If-None-Match header with the correct ETag,
            // return 304 Not Modified.
            var etag = "\"" + Convert.ToHexString(SHA1.HashData(pathBytes)).ToLower() + "\"";
            if (ctx.Request.Headers["If-None-Match"] == etag)
            {
                ctx.Response.StatusCode = 304;
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("ETag", etag);
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = pathBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(pathBytes);
        });
    }

    [Test(Description = "Perform a download")]
    [CancelAfter(30_000)]
    public async Task Download(CancellationToken ct)
    {
        using var httpServer = EchoServer();
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            NullDownloadValidator.Instance, ct);
        await dlTask.Task;
        Assert.That(dlTask.BytesTotal, Is.EqualTo(4));
        Assert.That(dlTask.BytesWritten, Is.EqualTo(4));
        Assert.That(dlTask.Progress, Is.EqualTo(1));
        Assert.That(dlTask.IsCompleted, Is.True);
        Assert.That(await File.ReadAllTextAsync(destPath, ct), Is.EqualTo("test"));
    }

    [Test(Description = "Perform 2 downloads with the same destination")]
    [CancelAfter(30_000)]
    public async Task DownloadSameDest(CancellationToken ct)
    {
        using var httpServer = EchoServer();
        var url0 = new Uri(httpServer.BaseUrl + "/test0");
        var url1 = new Uri(httpServer.BaseUrl + "/test1");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var startTask0 = manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url0), destPath,
            NullDownloadValidator.Instance, ct);
        var startTask1 = manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url1), destPath,
            NullDownloadValidator.Instance, ct);
        var dlTask0 = await startTask0;
        await dlTask0.Task;
        Assert.That(dlTask0.BytesTotal, Is.EqualTo(5));
        Assert.That(dlTask0.BytesWritten, Is.EqualTo(5));
        Assert.That(dlTask0.Progress, Is.EqualTo(1));
        Assert.That(dlTask0.IsCompleted, Is.True);
        var dlTask1 = await startTask1;
        await dlTask1.Task;
        Assert.That(dlTask1.BytesTotal, Is.EqualTo(5));
        Assert.That(dlTask1.BytesWritten, Is.EqualTo(5));
        Assert.That(dlTask1.Progress, Is.EqualTo(1));
        Assert.That(dlTask1.IsCompleted, Is.True);
    }

    [Test(Description = "Download with X-Original-Content-Length")]
    [CancelAfter(30_000)]
    public async Task DownloadWithXOriginalContentLength(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("X-Original-Content-Length", "4");
            ctx.Response.ContentType = "text/plain";
            // Don't set Content-Length.
            await ctx.Response.OutputStream.WriteAsync("test"u8.ToArray(), ct);
            await ctx.Response.OutputStream.FlushAsync(ct);
        });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");
        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var dlTask = await manager.StartDownloadAsync(req, destPath, NullDownloadValidator.Instance, ct);

        await dlTask.Task;
        Assert.That(dlTask.BytesTotal, Is.EqualTo(4));
        Assert.That(dlTask.BytesWritten, Is.EqualTo(4));
    }

    [Test(Description = "Download with mismatched Content-Length")]
    [CancelAfter(30_000)]
    public async Task DownloadWithMismatchedContentLength(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("X-Original-Content-Length", "5"); // incorrect
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.OutputStream.WriteAsync("test"u8.ToArray(), ct);
            await ctx.Response.OutputStream.FlushAsync(ct);
        });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");
        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var dlTask = await manager.StartDownloadAsync(req, destPath, NullDownloadValidator.Instance, ct);

        var ex = Assert.ThrowsAsync<IOException>(() => dlTask.Task);
        Assert.That(ex.Message, Is.EqualTo("Downloaded file size does not match expected response content length: Expected=5, BytesWritten=4"));
    }

    [Test(Description = "Download with custom headers")]
    [CancelAfter(30_000)]
    public async Task WithHeaders(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(ctx =>
        {
            Assert.That(ctx.Request.Headers["X-Custom-Header"], Is.EqualTo("custom-value"));
            ctx.Response.StatusCode = 200;
        });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Custom-Header", "custom-value");
        var dlTask = await manager.StartDownloadAsync(req, destPath, NullDownloadValidator.Instance, ct);
        await dlTask.Task;
    }

    [Test(Description = "Perform a download against an existing identical file")]
    [CancelAfter(30_000)]
    public async Task DownloadExisting(CancellationToken ct)
    {
        using var httpServer = EchoServer();
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        // Create the destination file with a very old timestamp.
        await File.WriteAllTextAsync(destPath, "test", ct);
        File.SetLastWriteTime(destPath, DateTime.Now - TimeSpan.FromDays(365));

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            NullDownloadValidator.Instance, ct);
        await dlTask.Task;
        Assert.That(dlTask.BytesWritten, Is.Zero);
        Assert.That(await File.ReadAllTextAsync(destPath, ct), Is.EqualTo("test"));
        Assert.That(File.GetLastWriteTime(destPath), Is.LessThan(DateTime.Now - TimeSpan.FromDays(1)));
    }

    [Test(Description = "Perform a download against an existing file with different content")]
    [CancelAfter(30_000)]
    public async Task DownloadExistingDifferentContent(CancellationToken ct)
    {
        using var httpServer = EchoServer();
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        // Create the destination file with a very old timestamp.
        await File.WriteAllTextAsync(destPath, "TEST", ct);
        File.SetLastWriteTime(destPath, DateTime.Now - TimeSpan.FromDays(365));

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            NullDownloadValidator.Instance, ct);
        await dlTask.Task;
        Assert.That(dlTask.BytesWritten, Is.EqualTo(4));
        Assert.That(await File.ReadAllTextAsync(destPath, ct), Is.EqualTo("test"));
        Assert.That(File.GetLastWriteTime(destPath), Is.GreaterThan(DateTime.Now - TimeSpan.FromDays(1)));
    }

    [Test(Description = "Unexpected response code from server")]
    [CancelAfter(30_000)]
    public async Task UnexpectedResponseCode(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(ctx => { ctx.Response.StatusCode = 404; });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        // The "inner" Task should fail.
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            NullDownloadValidator.Instance, ct);
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await dlTask.Task);
        Assert.That(ex.Message, Does.Contain("404"));
    }

    // TODO: It would be nice to have a test that tests mismatched
    //       Content-Length, but it seems HttpListener doesn't allow that.

    [Test(Description = "Mismatched ETag")]
    [CancelAfter(30_000)]
    public async Task MismatchedETag(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("ETag", "\"beef\"");
        });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        // The "inner" Task should fail.
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            NullDownloadValidator.Instance, ct);
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await dlTask.Task);
        Assert.That(ex.Message, Does.Contain("ETag does not match SHA1 hash of downloaded file").And.Contains("beef"));
    }

    [Test(Description = "Timeout waiting for existing download")]
    [CancelAfter(30_000)]
    public async Task CancelledWaitingForOther(CancellationToken ct)
    {
        var testCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var httpServer = new TestHttpServer(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), testCts.Token);
        });
        var url0 = new Uri(httpServer.BaseUrl + "/test0");
        var url1 = new Uri(httpServer.BaseUrl + "/test1");
        var destPath = Path.Combine(_tempDir, "test");
        var manager = new Downloader(NullLogger<Downloader>.Instance);

        // first outer task succeeds, getting download started
        var dlTask0 = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url0), destPath,
            NullDownloadValidator.Instance, testCts.Token);

        // The second request fails if the timeout is short
        var smallerCt = new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token;
        Assert.ThrowsAsync<TaskCanceledException>(async () => await manager.StartDownloadAsync(
            new HttpRequestMessage(HttpMethod.Get, url1), destPath,
            NullDownloadValidator.Instance, smallerCt));
        await testCts.CancelAsync();
    }

    [Test(Description = "Timeout on response body")]
    [CancelAfter(30_000)]
    public async Task CancelledInner(CancellationToken ct)
    {
        var httpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var taskCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var httpServer = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.OutputStream.WriteAsync("test"u8.ToArray(), httpCts.Token);
            await ctx.Response.OutputStream.FlushAsync(httpCts.Token);
            // wait up to 5 seconds.
            await Task.Delay(TimeSpan.FromSeconds(5), httpCts.Token);
        });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        // The "inner" Task should fail.
        var taskCt = taskCts.Token;
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            NullDownloadValidator.Instance, taskCt);
        await taskCts.CancelAsync();
        var ex = Assert.ThrowsAsync<TaskCanceledException>(async () => await dlTask.Task);
        Assert.That(ex.CancellationToken, Is.EqualTo(taskCt));
        await httpCts.CancelAsync();
    }

    [Test(Description = "Validation failure")]
    [CancelAfter(30_000)]
    public async Task ValidationFailure(CancellationToken ct)
    {
        using var httpServer = EchoServer();
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            new TestDownloadValidator(new Exception("test exception")), ct);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await dlTask.Task);
        Assert.That(ex.Message, Does.Contain("Downloaded file failed validation"));
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException!.Message, Is.EqualTo("test exception"));
    }

    [Test(Description = "Validation failure on existing file")]
    [CancelAfter(30_000)]
    public async Task ValidationFailureExistingFile(CancellationToken ct)
    {
        using var httpServer = EchoServer();
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");
        await File.WriteAllTextAsync(destPath, "test", ct);

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            new TestDownloadValidator(new Exception("test exception")), ct);
        // The "inner" Task should fail.
        var ex = Assert.ThrowsAsync<Exception>(async () => { await dlTask.Task; });
        Assert.That(ex.Message, Does.Contain("Existing file failed validation"));
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException!.Message, Is.EqualTo("test exception"));
    }
}

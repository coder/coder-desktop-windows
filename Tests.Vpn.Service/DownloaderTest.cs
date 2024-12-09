using System.Security.Cryptography;
using System.Text;
using Coder.Desktop.Vpn.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coder.Desktop.Tests.Vpn.Service;

public class TestDownloadValidator(Exception e) : IDownloadValidator
{
    public Task ValidateAsync(string path, CancellationToken ct = default)
    {
        throw e;
    }
}

[TestFixture]
public class AuthenticodeDownloadValidatorTest
{
    [Test(Description = "Test an unsigned binary")]
    [CancelAfter(30_000)]
    public void Unsigned(CancellationToken ct)
    {
        // TODO: this
    }

    [Test(Description = "Test an untrusted binary")]
    [CancelAfter(30_000)]
    public void Untrusted(CancellationToken ct)
    {
        // TODO: this
    }

    [Test(Description = "Test an binary with a detached signature (catalog file)")]
    [CancelAfter(30_000)]
    public void DifferentCertTrusted(CancellationToken ct)
    {
        // notepad.exe uses a catalog file for its signature.
        var ex = Assert.ThrowsAsync<Exception>(() =>
            AuthenticodeDownloadValidator.Coder.ValidateAsync(@"C:\Windows\System32\notepad.exe", ct));
        Assert.That(ex.Message,
            Does.Contain("File is not signed with an embedded Authenticode signature: Kind=Catalog"));
    }

    [Test(Description = "Test a binary signed by a different certificate")]
    [CancelAfter(30_000)]
    public void DifferentCertUntrusted(CancellationToken ct)
    {
        // TODO: this
    }

    [Test(Description = "Test a binary signed by Coder's certificate")]
    [CancelAfter(30_000)]
    public async Task CoderSigned(CancellationToken ct)
    {
        // TODO: this
        await Task.CompletedTask;
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
        Assert.That(dlTask.TotalBytes, Is.EqualTo(4));
        Assert.That(dlTask.BytesRead, Is.EqualTo(4));
        Assert.That(dlTask.Progress, Is.EqualTo(1));
        Assert.That(dlTask.IsCompleted, Is.True);
        Assert.That(await File.ReadAllTextAsync(destPath, ct), Is.EqualTo("test"));
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
        Assert.That(dlTask.BytesRead, Is.Zero);
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
        Assert.That(dlTask.BytesRead, Is.EqualTo(4));
        Assert.That(await File.ReadAllTextAsync(destPath, ct), Is.EqualTo("test"));
        Assert.That(File.GetLastWriteTime(destPath), Is.GreaterThan(DateTime.Now - TimeSpan.FromDays(1)));
    }

    [Test(Description = "Unexpected response code from server")]
    [CancelAfter(30_000)]
    public void UnexpectedResponseCode(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(ctx => { ctx.Response.StatusCode = 404; });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        // The "outer" Task should fail.
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
                NullDownloadValidator.Instance, ct));
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

    [Test(Description = "Timeout on response headers")]
    [CancelAfter(30_000)]
    public void CancelledOuter(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(async _ => { await Task.Delay(TimeSpan.FromSeconds(5), ct); });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        // The "outer" Task should fail.
        var smallerCt = new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token;
        Assert.ThrowsAsync<TaskCanceledException>(
            async () => await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
                NullDownloadValidator.Instance, smallerCt));
    }

    [Test(Description = "Timeout on response body")]
    [CancelAfter(30_000)]
    public async Task CancelledInner(CancellationToken ct)
    {
        using var httpServer = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.OutputStream.WriteAsync("test"u8.ToArray(), ct);
            await ctx.Response.OutputStream.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        });
        var url = new Uri(httpServer.BaseUrl + "/test");
        var destPath = Path.Combine(_tempDir, "test");

        var manager = new Downloader(NullLogger<Downloader>.Instance);
        // The "inner" Task should fail.
        var smallerCt = new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token;
        var dlTask = await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
            NullDownloadValidator.Instance, smallerCt);
        var ex = Assert.ThrowsAsync<TaskCanceledException>(async () => await dlTask.Task);
        Assert.That(ex.CancellationToken, Is.EqualTo(smallerCt));
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
        // The "outer" Task should fail because the inner task never starts.
        var ex = Assert.ThrowsAsync<Exception>(async () =>
        {
            await manager.StartDownloadAsync(new HttpRequestMessage(HttpMethod.Get, url), destPath,
                new TestDownloadValidator(new Exception("test exception")), ct);
        });
        Assert.That(ex.Message, Does.Contain("Existing file failed validation"));
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException!.Message, Is.EqualTo("test exception"));
    }
}

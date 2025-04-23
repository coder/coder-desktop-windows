using System.Diagnostics;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Moq;

namespace Coder.Desktop.Tests.App.Services;

[TestFixture]
public class CredentialManagerTest
{
    private const string TestServerUrl = "https://dev.coder.com";
    private const string TestApiToken = "abcdef1234-abcdef1234567890ABCDEF";
    private const string TestUsername = "dean";

    [Test(Description = "End to end test with WindowsCredentialBackend")]
    [CancelAfter(30_000)]
    public async Task EndToEnd(CancellationToken ct)
    {
        var credentialBackend = new WindowsCredentialBackend($"Coder.Desktop.Test.App.{Guid.NewGuid()}");

        // I lied. It's not fully end to end. We don't use a real or fake API
        // server for this and use a mock client instead.
        var apiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        apiClient.Setup(x => x.SetSessionToken(TestApiToken));
        apiClient.Setup(x => x.GetBuildInfo(It.IsAny<CancellationToken>()).Result)
            .Returns(new BuildInfo { Version = "v2.20.0" });
        apiClient.Setup(x => x.GetUser(User.Me, It.IsAny<CancellationToken>()).Result)
            .Returns(new User { Username = TestUsername });
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        apiClientFactory.Setup(x => x.Create(TestServerUrl))
            .Returns(apiClient.Object);

        try
        {
            var manager1 = new CredentialManager(credentialBackend, apiClientFactory.Object);

            // Cached credential should be unknown.
            var cred = manager1.GetCachedCredentials();
            Assert.That(cred.State, Is.EqualTo(CredentialState.Unknown));

            // Load credentials from backend. No credentials are stored so it
            // should be invalid.
            cred = await manager1.LoadCredentials(ct).WaitAsync(ct);
            Assert.That(cred.State, Is.EqualTo(CredentialState.Invalid));

            // SetCredentials should succeed.
            await manager1.SetCredentials(TestServerUrl, TestApiToken, ct).WaitAsync(ct);

            // Cached credential should be valid.
            cred = manager1.GetCachedCredentials();
            Assert.That(cred.State, Is.EqualTo(CredentialState.Valid));
            Assert.That(cred.CoderUrl, Is.EqualTo(TestServerUrl));
            Assert.That(cred.ApiToken, Is.EqualTo(TestApiToken));
            Assert.That(cred.Username, Is.EqualTo(TestUsername));

            // Load credentials should return the same reference.
            var loadedCred = await manager1.LoadCredentials(ct).WaitAsync(ct);
            Assert.That(ReferenceEquals(cred, loadedCred), Is.True);

            // A second manager should be able to load the same credentials.
            var manager2 = new CredentialManager(credentialBackend, apiClientFactory.Object);
            cred = await manager2.LoadCredentials(ct).WaitAsync(ct);
            Assert.That(cred.State, Is.EqualTo(CredentialState.Valid));
            Assert.That(cred.CoderUrl, Is.EqualTo(TestServerUrl));
            Assert.That(cred.ApiToken, Is.EqualTo(TestApiToken));
            Assert.That(cred.Username, Is.EqualTo(TestUsername));

            // Clearing the credentials should make them invalid.
            await manager1.ClearCredentials(ct).WaitAsync(ct);
            cred = manager1.GetCachedCredentials();
            Assert.That(cred.State, Is.EqualTo(CredentialState.Invalid));

            // And loading them in a new manager should also be invalid.
            var manager3 = new CredentialManager(credentialBackend, apiClientFactory.Object);
            cred = await manager3.LoadCredentials(ct).WaitAsync(ct);
            Assert.That(cred.State, Is.EqualTo(CredentialState.Invalid));
        }
        finally
        {
            // In case something goes wrong, make sure to clean up.
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(15_000);
            await credentialBackend.DeleteCredentials(cts.Token);
        }
    }

    [Test(Description = "Test SetCredentials with invalid URL or token")]
    [CancelAfter(30_000)]
    public void SetCredentialsInvalidUrlOrToken(CancellationToken ct)
    {
        var credentialBackend = new Mock<ICredentialBackend>(MockBehavior.Strict);
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        var manager = new CredentialManager(credentialBackend.Object, apiClientFactory.Object);

        var cases = new List<(string, string, string)>
        {
            (null!, TestApiToken, "Coder URL is required"),
            ("", TestApiToken, "Coder URL is required"),
            (" ", TestApiToken, "Coder URL is required"),
            (new string('a', 129), TestApiToken, "Coder URL is too long"),
            ("a", TestApiToken, "not a valid URL"),
            ("ftp://dev.coder.com", TestApiToken, "Coder URL must be HTTP or HTTPS"),

            (TestServerUrl, null!, "API token is required"),
            (TestServerUrl, "", "API token is required"),
            (TestServerUrl, " ", "API token is required"),
        };

        foreach (var (url, token, expectedMessage) in cases)
        {
            var ex = Assert.ThrowsAsync<ArgumentException>(() =>
                manager.SetCredentials(url, token, ct));
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }
    }

    [Test(Description = "Invalid server buildinfo response")]
    [CancelAfter(30_000)]
    public void InvalidServerBuildInfoResponse(CancellationToken ct)
    {
        var credentialBackend = new Mock<ICredentialBackend>(MockBehavior.Strict);
        var apiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        apiClient.Setup(x => x.GetBuildInfo(It.IsAny<CancellationToken>()).Result)
            .Throws(new Exception("Test exception"));
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        apiClientFactory.Setup(x => x.Create(TestServerUrl))
            .Returns(apiClient.Object);

        // Attempt a set.
        var manager = new CredentialManager(credentialBackend.Object, apiClientFactory.Object);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SetCredentials(TestServerUrl, TestApiToken, ct));
        Assert.That(ex.Message, Does.Contain("Could not connect to or verify Coder server"));

        // Attempt a load.
        credentialBackend.Setup(x => x.ReadCredentials(It.IsAny<CancellationToken>()).Result)
            .Returns(new RawCredentials
            {
                CoderUrl = TestServerUrl,
                ApiToken = TestApiToken,
            });
        var cred = manager.LoadCredentials(ct).Result;
        Assert.That(cred.State, Is.EqualTo(CredentialState.Invalid));
    }

    [Test(Description = "Invalid server version")]
    [CancelAfter(30_000)]
    public void InvalidServerVersion(CancellationToken ct)
    {
        var credentialBackend = new Mock<ICredentialBackend>(MockBehavior.Strict);
        var apiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        apiClient.Setup(x => x.GetBuildInfo(It.IsAny<CancellationToken>()).Result)
            .Returns(new BuildInfo { Version = "v2.19.0" });
        apiClient.Setup(x => x.SetSessionToken(TestApiToken));
        apiClient.Setup(x => x.GetUser(User.Me, It.IsAny<CancellationToken>()).Result)
            .Returns(new User { Username = TestUsername });
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        apiClientFactory.Setup(x => x.Create(TestServerUrl))
            .Returns(apiClient.Object);

        // Attempt a set.
        var manager = new CredentialManager(credentialBackend.Object, apiClientFactory.Object);
        var ex = Assert.ThrowsAsync<ArgumentException>(() =>
            manager.SetCredentials(TestServerUrl, TestApiToken, ct));
        Assert.That(ex.Message, Does.Contain("not within required server version range"));

        // Attempt a load.
        credentialBackend.Setup(x => x.ReadCredentials(It.IsAny<CancellationToken>()).Result)
            .Returns(new RawCredentials
            {
                CoderUrl = TestServerUrl,
                ApiToken = TestApiToken,
            });
        var cred = manager.LoadCredentials(ct).Result;
        Assert.That(cred.State, Is.EqualTo(CredentialState.Invalid));
    }

    [Test(Description = "Invalid server user response")]
    [CancelAfter(30_000)]
    public void InvalidServerUserResponse(CancellationToken ct)
    {
        var credentialBackend = new Mock<ICredentialBackend>(MockBehavior.Strict);
        var apiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        apiClient.Setup(x => x.GetBuildInfo(It.IsAny<CancellationToken>()).Result)
            .Returns(new BuildInfo { Version = "v2.20.0" });
        apiClient.Setup(x => x.SetSessionToken(TestApiToken));
        apiClient.Setup(x => x.GetUser(User.Me, It.IsAny<CancellationToken>()).Result)
            .Throws(new Exception("Test exception"));
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        apiClientFactory.Setup(x => x.Create(TestServerUrl))
            .Returns(apiClient.Object);

        // Attempt a set.
        var manager = new CredentialManager(credentialBackend.Object, apiClientFactory.Object);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SetCredentials(TestServerUrl, TestApiToken, ct));
        Assert.That(ex.Message, Does.Contain("Could not connect to or verify Coder server"));

        // Attempt a load.
        credentialBackend.Setup(x => x.ReadCredentials(It.IsAny<CancellationToken>()).Result)
            .Returns(new RawCredentials
            {
                CoderUrl = TestServerUrl,
                ApiToken = TestApiToken,
            });
        var cred = manager.LoadCredentials(ct).Result;
        Assert.That(cred.State, Is.EqualTo(CredentialState.Invalid));
    }

    [Test(Description = "Invalid username")]
    [CancelAfter(30_000)]
    public void InvalidUsername(CancellationToken ct)
    {
        var credentialBackend = new Mock<ICredentialBackend>(MockBehavior.Strict);
        var apiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        apiClient.Setup(x => x.GetBuildInfo(It.IsAny<CancellationToken>()).Result)
            .Returns(new BuildInfo { Version = "v2.20.0" });
        apiClient.Setup(x => x.SetSessionToken(TestApiToken));
        apiClient.Setup(x => x.GetUser(User.Me, It.IsAny<CancellationToken>()).Result)
            .Returns(new User { Username = "" });
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        apiClientFactory.Setup(x => x.Create(TestServerUrl))
            .Returns(apiClient.Object);

        // Attempt a set.
        var manager = new CredentialManager(credentialBackend.Object, apiClientFactory.Object);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SetCredentials(TestServerUrl, TestApiToken, ct));
        Assert.That(ex.Message, Does.Contain("username is empty"));

        // Attempt a load.
        credentialBackend.Setup(x => x.ReadCredentials(It.IsAny<CancellationToken>()).Result)
            .Returns(new RawCredentials
            {
                CoderUrl = TestServerUrl,
                ApiToken = TestApiToken,
            });
        var cred = manager.LoadCredentials(ct).Result;
        Assert.That(cred.State, Is.EqualTo(CredentialState.Invalid));
    }

    [Test(Description = "Duplicate loads should use the same Task")]
    [CancelAfter(30_000)]
    public async Task DuplicateLoads(CancellationToken ct)
    {
        var credentialBackend = new Mock<ICredentialBackend>(MockBehavior.Strict);
        credentialBackend.Setup(x => x.ReadCredentials(It.IsAny<CancellationToken>()).Result)
            .Returns(new RawCredentials
            {
                CoderUrl = TestServerUrl,
                ApiToken = TestApiToken,
            })
            .Verifiable(Times.Exactly(1));
        var apiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        // To accomplish delay, the GetBuildInfo will wait for a TCS.
        var tcs = new TaskCompletionSource();
        apiClient.Setup(x => x.GetBuildInfo(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken _) =>
            {
                await tcs.Task.WaitAsync(ct);
                return new BuildInfo { Version = "v2.20.0" };
            })
            .Verifiable(Times.Exactly(1));
        apiClient.Setup(x => x.SetSessionToken(TestApiToken));
        apiClient.Setup(x => x.GetUser(User.Me, It.IsAny<CancellationToken>()).Result)
            .Returns(new User { Username = TestUsername })
            .Verifiable(Times.Exactly(1));
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        apiClientFactory.Setup(x => x.Create(TestServerUrl))
            .Returns(apiClient.Object)
            .Verifiable(Times.Exactly(1));

        var manager = new CredentialManager(credentialBackend.Object, apiClientFactory.Object);
        var cred1Task = manager.LoadCredentials(ct);
        var cred2Task = manager.LoadCredentials(ct);
        Assert.That(ReferenceEquals(cred1Task, cred2Task), Is.True);
        tcs.SetResult();
        var cred1 = await cred1Task.WaitAsync(ct);
        var cred2 = await cred2Task.WaitAsync(ct);
        Assert.That(ReferenceEquals(cred1, cred2), Is.True);

        credentialBackend.Verify();
        apiClient.Verify();
        apiClientFactory.Verify();
    }

    [Test(Description = "A set during a load should cancel the load")]
    [CancelAfter(30_000)]
    public async Task SetDuringLoad(CancellationToken ct)
    {
        var credentialBackend = new Mock<ICredentialBackend>(MockBehavior.Strict);
        // To accomplish a delay on the load, ReadCredentials will block on the CT.
        credentialBackend.Setup(x => x.ReadCredentials(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken innerCt) =>
            {
                await Task.Delay(Timeout.Infinite, innerCt).WaitAsync(ct);
                throw new UnreachableException();
            });
        credentialBackend.Setup(x =>
                x.WriteCredentials(
                    It.Is<RawCredentials>(c => c.CoderUrl == TestServerUrl && c.ApiToken == TestApiToken),
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var apiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        apiClient.Setup(x => x.GetBuildInfo(It.IsAny<CancellationToken>()).Result)
            .Returns(new BuildInfo { Version = "v2.20.0" });
        apiClient.Setup(x => x.SetSessionToken(TestApiToken));
        apiClient.Setup(x => x.GetUser(User.Me, It.IsAny<CancellationToken>()).Result)
            .Returns(new User { Username = TestUsername });
        var apiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        apiClientFactory.Setup(x => x.Create(TestServerUrl))
            .Returns(apiClient.Object);

        var manager = new CredentialManager(credentialBackend.Object, apiClientFactory.Object);
        // Start a load...
        var loadTask = manager.LoadCredentials(ct);
        // Then fully perform a set.
        await manager.SetCredentials(TestServerUrl, TestApiToken, ct).WaitAsync(ct);
        // The load should have been cancelled.
        Assert.ThrowsAsync<TaskCanceledException>(() => loadTask);
    }
}

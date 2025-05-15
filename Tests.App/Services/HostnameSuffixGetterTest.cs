using System.ComponentModel.DataAnnotations;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.CoderSdk.Coder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog;

namespace Coder.Desktop.Tests.App.Services;

[TestFixture]
public class HostnameSuffixGetterTest
{
    const string coderUrl = "https://coder.test/";

    [SetUp]
    public void SetupMocks()
    {
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.NUnitOutput().CreateLogger();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSerilog();
        _logger = (ILogger<HostnameSuffixGetter>)builder.Build().Services
            .GetService(typeof(ILogger<HostnameSuffixGetter>))!;

        _mCoderApiClientFactory = new Mock<ICoderApiClientFactory>(MockBehavior.Strict);
        _mCredentialManager = new Mock<ICredentialManager>(MockBehavior.Strict);
        _mCoderApiClient = new Mock<ICoderApiClient>(MockBehavior.Strict);
        _mCoderApiClientFactory.Setup(m => m.Create(coderUrl)).Returns(_mCoderApiClient.Object);
    }

    private Mock<ICoderApiClientFactory> _mCoderApiClientFactory;
    private Mock<ICredentialManager> _mCredentialManager;
    private Mock<ICoderApiClient> _mCoderApiClient;
    private ILogger<HostnameSuffixGetter> _logger;

    [Test(Description = "Mainline no errors")]
    [CancelAfter(10_000)]
    public async Task Mainline(CancellationToken ct)
    {
        _mCredentialManager.Setup(m => m.GetCachedCredentials())
            .Returns(new CredentialModel() { State = CredentialState.Invalid });
        var hostnameSuffixGetter =
            new HostnameSuffixGetter(_mCredentialManager.Object, _mCoderApiClientFactory.Object, _logger);

        // initially, we return the default
        Assert.That(hostnameSuffixGetter.GetCachedSuffix(), Is.EqualTo(".coder"));

        // subscribed to suffix changes
        var suffixCompletion = new TaskCompletionSource<string>();
        hostnameSuffixGetter.SuffixChanged += (_, suffix) => suffixCompletion.SetResult(suffix);

        // set the client to return "test" as the suffix
        _mCoderApiClient.Setup(m => m.SetSessionToken("test-token"));
        _mCoderApiClient.Setup(m => m.GetAgentConnectionInfoGeneric(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new AgentConnectionInfo() { HostnameSuffix = "test" }));

        _mCredentialManager.Raise(m => m.CredentialsChanged += null, _mCredentialManager.Object, new CredentialModel
        {
            State = CredentialState.Valid,
            CoderUrl = new Uri(coderUrl),
            ApiToken = "test-token",
        });
        var gotSuffix = await TaskOrCancellation(suffixCompletion.Task, ct);
        Assert.That(gotSuffix, Is.EqualTo(".test"));

        // now, we should return the .test domain going forward
        Assert.That(hostnameSuffixGetter.GetCachedSuffix(), Is.EqualTo(".test"));
    }

    [Test(Description = "Retries if error")]
    [CancelAfter(30_000)]
    // TODO: make this test not have to actually wait for the retry.
    public async Task RetryError(CancellationToken ct)
    {
        _mCredentialManager.Setup(m => m.GetCachedCredentials())
            .Returns(new CredentialModel() { State = CredentialState.Invalid });
        var hostnameSuffixGetter =
            new HostnameSuffixGetter(_mCredentialManager.Object, _mCoderApiClientFactory.Object, _logger);

        // subscribed to suffix changes
        var suffixCompletion = new TaskCompletionSource<string>();
        hostnameSuffixGetter.SuffixChanged += (_, suffix) => suffixCompletion.SetResult(suffix);

        // set the client to fail once, then return successfully
        _mCoderApiClient.Setup(m => m.SetSessionToken("test-token"));
        var connectionInfoCompletion = new TaskCompletionSource<AgentConnectionInfo>();
        _mCoderApiClient.SetupSequence(m => m.GetAgentConnectionInfoGeneric(It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<AgentConnectionInfo>(new Exception("a bad thing happened")))
            .Returns(Task.FromResult(new AgentConnectionInfo() { HostnameSuffix = "test" }));

        _mCredentialManager.Raise(m => m.CredentialsChanged += null, _mCredentialManager.Object, new CredentialModel
        {
            State = CredentialState.Valid,
            CoderUrl = new Uri(coderUrl),
            ApiToken = "test-token",
        });
        var gotSuffix = await TaskOrCancellation(suffixCompletion.Task, ct);
        Assert.That(gotSuffix, Is.EqualTo(".test"));

        // now, we should return the .test domain going forward
        Assert.That(hostnameSuffixGetter.GetCachedSuffix(), Is.EqualTo(".test"));
    }

    /// <summary>
    ///     TaskOrCancellation waits for either the task to complete, or the given token to be canceled.
    /// </summary>
    internal static async Task<TResult> TaskOrCancellation<TResult>(Task<TResult> task,
        CancellationToken cancellationToken)
    {
        var cancellationTask = new TaskCompletionSource<TResult>();
        await using (cancellationToken.Register(() => cancellationTask.TrySetCanceled()))
        {
            // Wait for either the task or the cancellation
            var completedTask = await Task.WhenAny(task, cancellationTask.Task);
            // Await to propagate exceptions, if any
            return await completedTask;
        }
    }
}

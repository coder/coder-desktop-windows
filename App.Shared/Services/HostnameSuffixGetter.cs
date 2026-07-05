using System;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using Coder.Desktop.CoderSdk.Coder;
using Coder.Desktop.Vpn.Utilities;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.App.Services;

public class HostnameSuffixGetter : IHostnameSuffixGetter
{
    private const string DefaultSuffix = ".coder";

    private readonly ICredentialManager _credentialManager;
    private readonly ICoderApiClientFactory _clientFactory;
    private readonly ILogger<HostnameSuffixGetter> _logger;

    // _lock protects all private (non-readonly) values
    private readonly RaiiSemaphoreSlim _lock = new(1, 1);
    private string _domainSuffix = DefaultSuffix;
    private bool _dirty = false;
    private bool _getInProgress = false;
    private CredentialModel _credentialModel = new() { State = CredentialState.Invalid };

    public event EventHandler<string>? SuffixChanged;

    public HostnameSuffixGetter(ICredentialManager credentialManager, ICoderApiClientFactory apiClientFactory,
        ILogger<HostnameSuffixGetter> logger)
    {
        _credentialManager = credentialManager;
        _clientFactory = apiClientFactory;
        _logger = logger;
        credentialManager.CredentialsChanged += HandleCredentialsChanged;
        HandleCredentialsChanged(this, _credentialManager.GetCachedCredentials());
    }

    ~HostnameSuffixGetter()
    {
        _credentialManager.CredentialsChanged -= HandleCredentialsChanged;
    }

    private void HandleCredentialsChanged(object? sender, CredentialModel credentials)
    {
        using var _ = _lock.Lock();
        _logger.LogDebug("credentials updated with state {state}", credentials.State);
        _credentialModel = credentials;
        if (credentials.State != CredentialState.Valid) return;

        _dirty = true;
        if (!_getInProgress)
        {
            _getInProgress = true;
            Task.Run(Refresh).ContinueWith(MaybeRefreshAgain);
        }
    }

    private async Task Refresh()
    {
        _logger.LogDebug("refreshing domain suffix");
        CredentialModel credentials;
        using (_ = await _lock.LockAsync())
        {
            credentials = _credentialModel;
            if (credentials.State != CredentialState.Valid)
            {
                _logger.LogDebug("abandoning refresh because credentials are now invalid");
                return;
            }

            _dirty = false;
        }

        var client = _clientFactory.Create(credentials);
        using var timeoutSrc = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connInfo = await client.GetAgentConnectionInfoGeneric(timeoutSrc.Token);

        // older versions of Coder might not set this
        var suffix = string.IsNullOrEmpty(connInfo.HostnameSuffix)
            ? DefaultSuffix
            // and, it doesn't include the leading dot.
            : "." + connInfo.HostnameSuffix;

        var changed = false;
        using (_ = await _lock.LockAsync(CancellationToken.None))
        {
            if (_domainSuffix != suffix) changed = true;
            _domainSuffix = suffix;
        }

        if (changed)
        {
            _logger.LogInformation("got new domain suffix '{suffix}'", suffix);
            // grab a local copy of the EventHandler to avoid TOCTOU race on the `?.` null-check
            var del = SuffixChanged;
            del?.Invoke(this, suffix);
        }
        else
        {
            _logger.LogDebug("domain suffix unchanged '{suffix}'", suffix);
        }
    }

    private async Task MaybeRefreshAgain(Task prev)
    {
        if (prev.IsFaulted)
        {
            _logger.LogError(prev.Exception, "failed to query domain suffix");
            // back off here before retrying. We're just going to use a fixed, long
            // delay since this just affects UI stuff; we're not in a huge rush as
            // long as we eventually get the right value.
            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        using var l = await _lock.LockAsync(CancellationToken.None);
        if ((_dirty || prev.IsFaulted) && _credentialModel.State == CredentialState.Valid)
        {
            // we still have valid credentials and we're either dirty or the last Get failed.
            _logger.LogDebug("retrying domain suffix query");
            _ = Task.Run(Refresh).ContinueWith(MaybeRefreshAgain);
            return;
        }

        // Getting here means either the credentials are not valid or we don't need to
        // refresh anyway.
        // The next time we get new, valid credentials, HandleCredentialsChanged will kick off
        // a new Refresh
        _getInProgress = false;
        return;
    }

    public string GetCachedSuffix()
    {
        using var _ = _lock.Lock();
        return _domainSuffix;
    }
}

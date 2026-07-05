using System.Text.Json;
using Coder.Desktop.App.Models;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.CoderSdk.Coder;
using Coder.Desktop.Vpn.Utilities;

namespace Coder.Desktop.App.Services;

/// <summary>
///     Implements ICredentialManager using an ICredentialBackend to store
///     credentials.
/// </summary>
public class CredentialManager : ICredentialManager
{
    private readonly ICredentialBackend Backend;
    private readonly ICoderApiClientFactory CoderApiClientFactory;

    // _opLock is held for the full duration of SetCredentials, and partially
    // during LoadCredentials. _opLock protects _inFlightLoad, _loadCts, and
    // writes to _latestCredentials.
    private readonly RaiiSemaphoreSlim _opLock = new(1, 1);

    // _inFlightLoad and _loadCts are set at the beginning of a LoadCredentials
    // call.
    private Task<CredentialModel>? _inFlightLoad;
    private CancellationTokenSource? _loadCts;

    // Reading and writing a reference in C# is always atomic, so this doesn't
    // need to be protected on reads with a lock in GetCachedCredentials.
    //
    // The volatile keyword disables optimizations on reads/writes which helps
    // other threads see the new value quickly (no guarantee that it's
    // immediate).
    private volatile CredentialModel? _latestCredentials;

    public CredentialManager(ICredentialBackend backend, ICoderApiClientFactory coderApiClientFactory)
    {
        Backend = backend;
        CoderApiClientFactory = coderApiClientFactory;
    }

    public event EventHandler<CredentialModel>? CredentialsChanged;

    public CredentialModel GetCachedCredentials()
    {
        // No lock required to read the reference.
        var latestCreds = _latestCredentials;
        // No clone needed as the model is immutable.
        if (latestCreds != null) return latestCreds;

        return new CredentialModel
        {
            State = CredentialState.Unknown,
        };
    }

    // Implements ICoderApiClientCredentialProvider
    public CoderApiClientCredential? GetCoderApiClientCredential()
    {
        var latestCreds = _latestCredentials;
        if (latestCreds is not { State: CredentialState.Valid } || latestCreds.CoderUrl is null)
            return null;

        return new CoderApiClientCredential
        {
            CoderUrl = latestCreds.CoderUrl,
            ApiToken = latestCreds.ApiToken ?? "",
        };
    }

    public async Task<string?> GetSignInUri()
    {
        try
        {
            var raw = await Backend.ReadCredentials();
            if (raw is not null && !string.IsNullOrWhiteSpace(raw.CoderUrl)) return raw.CoderUrl;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    // LoadCredentials may be preempted by SetCredentials.
    public Task<CredentialModel> LoadCredentials(CancellationToken ct = default)
    {
        // This function is not `async` because we may return an existing task.
        // However, we still want to acquire the lock with the
        // CancellationToken so it can be canceled if needed.
        using var _ = _opLock.LockAsync(ct).Result;

        // If we already have a cached value, return it.
        var latestCreds = _latestCredentials;
        if (latestCreds != null) return Task.FromResult(latestCreds);

        // If we are already loading, return the existing task.
        if (_inFlightLoad != null) return _inFlightLoad;

        // Otherwise, kick off a new load.
        // Note: subsequent loads returned from above will ignore the passed in
        // CancellationToken. We set a maximum timeout of 15 seconds anyway.
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loadCts.CancelAfter(TimeSpan.FromSeconds(15));
        _inFlightLoad = LoadCredentialsInner(_loadCts.Token);
        return _inFlightLoad;
    }

    public async Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct)
    {
        using var _ = await _opLock.LockAsync(ct);

        // If there's an ongoing load, cancel it.
        if (_loadCts != null)
        {
            await _loadCts.CancelAsync();
            _loadCts.Dispose();
            _loadCts = null;
            _inFlightLoad = null;
        }

        if (string.IsNullOrWhiteSpace(coderUrl)) throw new ArgumentException("Coder URL is required", nameof(coderUrl));
        coderUrl = coderUrl.Trim();
        if (coderUrl.Length > 128) throw new ArgumentException("Coder URL is too long", nameof(coderUrl));
        if (!Uri.TryCreate(coderUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Coder URL '{coderUrl}' is not a valid URL", nameof(coderUrl));
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException("Coder URL must be HTTP or HTTPS", nameof(coderUrl));
        if (uri.PathAndQuery != "/") throw new ArgumentException("Coder URL must be the root URL", nameof(coderUrl));
        if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("API token is required", nameof(apiToken));
        apiToken = apiToken.Trim();

        var raw = new RawCredentials
        {
            CoderUrl = coderUrl,
            ApiToken = apiToken,
        };
        var populateCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        populateCts.CancelAfter(TimeSpan.FromSeconds(15));
        var model = await PopulateModel(raw, populateCts.Token);
        await Backend.WriteCredentials(raw, ct);
        UpdateState(model);
    }

    public async Task ClearCredentials(CancellationToken ct = default)
    {
        using var _ = await _opLock.LockAsync(ct);
        await Backend.DeleteCredentials(ct);
        UpdateState(new CredentialModel
        {
            State = CredentialState.Invalid,
        });
    }

    private async Task<CredentialModel> LoadCredentialsInner(CancellationToken ct)
    {
        CredentialModel model;
        try
        {
            var raw = await Backend.ReadCredentials(ct);
            model = await PopulateModel(raw, ct);
        }
        catch
        {
            // This catch will be hit if a SetCredentials operation started, or
            // if the read/populate failed for some other reason (e.g. HTTP
            // timeout).
            //
            // We don't need to clear the credentials here, the app will think
            // they're unset and any subsequent SetCredentials call after the
            // user signs in again will overwrite the old invalid ones.
            model = new CredentialModel
            {
                State = CredentialState.Invalid,
            };
        }

        // Grab the lock again so we can update the state. Don't use the CT
        // here as it may have already been canceled.
        using (await _opLock.LockAsync(TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            // Prevent new LoadCredentials calls from returning this task.
            if (_loadCts != null)
            {
                _loadCts.Dispose();
                _loadCts = null;
                _inFlightLoad = null;
            }

            // If we were canceled but made it this far, try to return the
            // latest credentials instead.
            if (ct.IsCancellationRequested)
            {
                var latestCreds = _latestCredentials;
                if (latestCreds is not null) return latestCreds;
            }

            UpdateState(model);
            ct.ThrowIfCancellationRequested();
            return model;
        }
    }

    private async Task<CredentialModel> PopulateModel(RawCredentials? credentials, CancellationToken ct)
    {
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.CoderUrl) ||
            string.IsNullOrWhiteSpace(credentials.ApiToken))
            return new CredentialModel
            {
                State = CredentialState.Invalid,
            };

        if (!Uri.TryCreate(credentials.CoderUrl, UriKind.Absolute, out var uri))
            return new CredentialModel
            {
                State = CredentialState.Invalid,
            };

        BuildInfo buildInfo;
        User me;
        try
        {
            var sdkClient = CoderApiClientFactory.Create(credentials.CoderUrl);
            // BuildInfo does not require authentication.
            buildInfo = await sdkClient.GetBuildInfo(ct);
            sdkClient.SetSessionToken(credentials.ApiToken);
            me = await sdkClient.GetUser(User.Me, ct);
        }
        catch (CoderApiHttpException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Could not connect to or verify Coder server", e);
        }

        ServerVersionUtilities.ParseAndValidateServerVersion(buildInfo.Version);
        if (string.IsNullOrWhiteSpace(me.Username))
            throw new InvalidOperationException("Could not retrieve user information, username is empty");

        return new CredentialModel
        {
            State = CredentialState.Valid,
            CoderUrl = uri,
            ApiToken = credentials.ApiToken,
            Username = me.Username,
        };
    }

    // Lock must be held when calling this function.
    private void UpdateState(CredentialModel newModel)
    {
        _latestCredentials = newModel;
        // Since the event handlers could block (or call back the
        // CredentialManager and deadlock), we run these in a new task.
        if (CredentialsChanged == null) return;
        Task.Run(() => { CredentialsChanged?.Invoke(this, newModel); });
    }
}

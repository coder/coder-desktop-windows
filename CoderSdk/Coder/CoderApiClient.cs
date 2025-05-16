using System.Text.Json.Serialization;

namespace Coder.Desktop.CoderSdk.Coder;

public interface ICoderApiClientFactory
{
    public ICoderApiClient Create(string baseUrl);
    public ICoderApiClient Create(ICoderApiClientCredentialProvider provider);
}

public class CoderApiClientCredential
{
    public required Uri CoderUrl { get; set; }
    public required string ApiToken { get; set; }
}

public interface ICoderApiClientCredentialProvider
{
    public CoderApiClientCredential? GetCoderApiClientCredential();
}

public class CoderApiClientFactory : ICoderApiClientFactory
{
    public ICoderApiClient Create(string baseUrl)
    {
        return new CoderApiClient(baseUrl);
    }

    public ICoderApiClient Create(ICoderApiClientCredentialProvider provider)
    {
        var cred = provider.GetCoderApiClientCredential();
        if (cred == null)
            throw new InvalidOperationException(
                "Cannot create Coder API client with invalid credential provider: credential is null");

        var client = Create(cred.CoderUrl);
        client.SetSessionToken(cred.ApiToken);
        return client;
    }

    public ICoderApiClient Create(Uri baseUrl)
    {
        return new CoderApiClient(baseUrl);
    }
}

public partial interface ICoderApiClient
{
    public void SetSessionToken(string token);
}

[JsonSerializable(typeof(AgentConnectionInfo))]
[JsonSerializable(typeof(BuildInfo))]
[JsonSerializable(typeof(Response))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(ValidationError))]
[JsonSerializable(typeof(WorkspaceAgent))]
[JsonSerializable(typeof(WorkspaceApp))]
public partial class CoderApiJsonContext : JsonSerializerContext;

/// <summary>
///     Provides a limited selection of API methods for a Coder instance.
/// </summary>
public partial class CoderApiClient : ICoderApiClient
{
    private const string SessionTokenHeader = "Coder-Session-Token";

    private readonly JsonHttpClient _httpClient;

    public CoderApiClient(string baseUrl) : this(new Uri(baseUrl, UriKind.Absolute))
    {
    }

    public CoderApiClient(Uri baseUrl)
    {
        if (baseUrl.PathAndQuery != "/")
            throw new ArgumentException($"Base URL '{baseUrl}' must not contain a path", nameof(baseUrl));
        _httpClient = new JsonHttpClient(baseUrl, CoderApiJsonContext.Default);
    }

    public CoderApiClient(string baseUrl, string token) : this(baseUrl)
    {
        SetSessionToken(token);
    }

    public void SetSessionToken(string token)
    {
        _httpClient.RemoveHeader(SessionTokenHeader);
        _httpClient.SetHeader(SessionTokenHeader, token);
    }

    private async Task<TResponse> SendRequestNoBodyAsync<TResponse>(HttpMethod method, string path,
        CancellationToken ct = default)
    {
        return await SendRequestAsync<object, TResponse>(method, path, null, ct);
    }

    private Task<TResponse> SendRequestAsync<TRequest, TResponse>(HttpMethod method, string path,
        TRequest? payload, CancellationToken ct = default)
    {
        return _httpClient.SendRequestAsync<TRequest, TResponse>(method, path, payload, ct);
    }
}

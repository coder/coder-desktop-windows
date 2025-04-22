using System.Text.Json.Serialization;

namespace Coder.Desktop.CoderSdk.Agent;

public interface IAgentApiClientFactory
{
    public IAgentApiClient Create(string hostname);
}

public class AgentApiClientFactory : IAgentApiClientFactory
{
    public IAgentApiClient Create(string hostname)
    {
        return new AgentApiClient(hostname);
    }
}

public partial interface IAgentApiClient
{
}

[JsonSerializable(typeof(ListDirectoryRequest))]
[JsonSerializable(typeof(ListDirectoryResponse))]
[JsonSerializable(typeof(Response))]
public partial class AgentApiJsonContext : JsonSerializerContext;

public partial class AgentApiClient : IAgentApiClient
{
    private const int AgentApiPort = 4;

    private readonly JsonHttpClient _httpClient;

    public AgentApiClient(string hostname) : this(new UriBuilder
    {
        Scheme = "http",
        Host = hostname,
        Port = AgentApiPort,
        Path = "/",
    }.Uri)
    {
    }

    public AgentApiClient(Uri baseUrl)
    {
        if (baseUrl.PathAndQuery != "/")
            throw new ArgumentException($"Base URL '{baseUrl}' must not contain a path", nameof(baseUrl));
        _httpClient = new JsonHttpClient(baseUrl, AgentApiJsonContext.Default);
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

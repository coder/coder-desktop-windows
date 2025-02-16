using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoderSdk;

/// <summary>
///     Changes names from PascalCase to snake_case.
/// </summary>
internal class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        return string.Concat(
            name.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + char.ToLower(x) : char.ToLower(x).ToString())
        );
    }
}

[JsonSerializable(typeof(BuildInfo))]
[JsonSerializable(typeof(User))]
public partial class CoderSdkJsonContext : JsonSerializerContext
{
}

/// <summary>
///     Provides a limited selection of API methods for a Coder instance.
/// </summary>
public partial class CoderApiClient
{
    // TODO: allow adding headers
    private readonly HttpClient _httpClient = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public CoderApiClient(string baseUrl) : this(new Uri(baseUrl, UriKind.Absolute))
    {
    }

    public CoderApiClient(Uri baseUrl)
    {
        if (baseUrl.PathAndQuery != "/")
            throw new ArgumentException($"Base URL '{baseUrl}' must not contain a path", nameof(baseUrl));
        _httpClient.BaseAddress = baseUrl;
        _jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = CoderSdkJsonContext.Default,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public CoderApiClient(string baseUrl, string token) : this(baseUrl)
    {
        SetSessionToken(token);
    }

    public void SetSessionToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Remove("Coder-Session-Token");
        _httpClient.DefaultRequestHeaders.Add("Coder-Session-Token", token);
    }
    private async Task<TResponse> SendRequestNoBodyAsync<TResponse>(HttpMethod method, string path, CancellationToken ct = default) =>
        await SendRequestAsync<object, TResponse>(method, path, null, ct);

    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(HttpMethod method, string path,
        TRequest? payload, CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(method, path);

            if (payload is not null)
            {
                var json = JsonSerializer.Serialize(payload, typeof(TRequest), _jsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var res = await _httpClient.SendAsync(request, ct);
            // TODO: this should be improved to try and parse a codersdk.Error response
            res.EnsureSuccessStatusCode();

            var content = await res.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<TResponse>(content, _jsonOptions);
            if (data is null) throw new JsonException("Deserialized response is null");
            return data;
        }
        catch (Exception e)
        {
            throw new Exception($"API Request: {method} {path} (req body: {payload is not null})", e);
        }
    }
}

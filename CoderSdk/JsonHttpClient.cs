using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Coder.Desktop.CoderSdk;

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

internal class JsonHttpClient
{
    private readonly JsonSerializerOptions _jsonOptions;

    // TODO: allow users to add headers
    private readonly HttpClient _httpClient = new();

    public JsonHttpClient(Uri baseUri, IJsonTypeInfoResolver typeResolver)
    {
        _jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(new SnakeCaseNamingPolicy(), false));
        _httpClient.BaseAddress = baseUri;
    }

    public void RemoveHeader(string key)
    {
        _httpClient.DefaultRequestHeaders.Remove(key);
    }

    public void SetHeader(string key, string value)
    {
        _httpClient.DefaultRequestHeaders.Add(key, value);
    }

    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(HttpMethod method, string path,
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
            if (!res.IsSuccessStatusCode)
                throw await CoderApiHttpException.FromResponse(res, ct);

            var content = await res.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<TResponse>(content, _jsonOptions);
            if (data is null) throw new JsonException("Deserialized response is null");
            return data;
        }
        catch (CoderApiHttpException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new Exception($"API Request failed: {method} {path}", e);
        }
    }
}

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coder.Desktop.CoderSdk;

public class ValidationError
{
    public string Field { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class Response
{
    public string Message { get; set; } = "";
    public string Detail { get; set; } = "";
    public List<ValidationError> Validations { get; set; } = [];
}

[JsonSerializable(typeof(Response))]
[JsonSerializable(typeof(ValidationError))]
public partial class ErrorJsonContext : JsonSerializerContext;

public class CoderApiHttpException : Exception
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = ErrorJsonContext.Default,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Dictionary<HttpStatusCode, string> Helpers = new()
    {
        { HttpStatusCode.Unauthorized, "Try signing in again" },
    };

    public readonly HttpMethod? Method;
    public readonly Uri? RequestUri;
    public readonly HttpStatusCode StatusCode;
    public readonly string? ReasonPhrase;
    public readonly Response Response;

    public CoderApiHttpException(HttpMethod? method, Uri? requestUri, HttpStatusCode statusCode, string? reasonPhrase,
        Response response) : base(MessageFrom(method, requestUri, statusCode, reasonPhrase, response))
    {
        Method = method;
        RequestUri = requestUri;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Response = response;
    }

    public static async Task<CoderApiHttpException> FromResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        Response? responseObject;
        try
        {
            responseObject = JsonSerializer.Deserialize<Response>(content, JsonOptions);
        }
        catch (JsonException)
        {
            responseObject = null;
        }

        if (responseObject is null or { Message: null or "" })
            responseObject = new Response
            {
                Message = "Could not parse response, or response has no message",
                Detail = content,
                Validations = [],
            };

        return new CoderApiHttpException(
            response.RequestMessage?.Method,
            response.RequestMessage?.RequestUri,
            response.StatusCode,
            response.ReasonPhrase,
            responseObject);
    }

    private static string MessageFrom(HttpMethod? method, Uri? requestUri, HttpStatusCode statusCode,
        string? reasonPhrase, Response response)
    {
        var message = $"Coder API Request: {method} '{requestUri}' failed with status code {(int)statusCode}";
        if (!string.IsNullOrEmpty(reasonPhrase)) message += $" {reasonPhrase}";
        message += $": {response.Message}";
        if (Helpers.TryGetValue(statusCode, out var helperMessage)) message += $": {helperMessage}";
        if (!string.IsNullOrEmpty(response.Detail)) message += $"\n\tError: {response.Detail}";
        foreach (var validation in response.Validations) message += $"\n\t{validation.Field}: {validation.Detail}";
        return message;
    }
}

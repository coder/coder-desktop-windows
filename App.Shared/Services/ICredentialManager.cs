using System.Text.Json.Serialization;
using Coder.Desktop.App.Models;
using Coder.Desktop.CoderSdk;
using Coder.Desktop.CoderSdk.Coder;

namespace Coder.Desktop.App.Services;

public class RawCredentials
{
    public required string CoderUrl { get; set; }
    public required string ApiToken { get; set; }
}

[JsonSerializable(typeof(RawCredentials))]
public partial class RawCredentialsJsonContext : JsonSerializerContext;

public interface ICredentialManager : ICoderApiClientCredentialProvider
{
    event EventHandler<CredentialModel> CredentialsChanged;
    CredentialModel GetCachedCredentials();
    Task<string?> GetSignInUri();
    Task<CredentialModel> LoadCredentials(CancellationToken ct = default);
    Task SetCredentials(string coderUrl, string apiToken, CancellationToken ct = default);
    Task ClearCredentials(CancellationToken ct = default);
}

public interface ICredentialBackend
{
    Task<RawCredentials?> ReadCredentials(CancellationToken ct = default);
    Task WriteCredentials(RawCredentials credentials, CancellationToken ct = default);
    Task DeleteCredentials(CancellationToken ct = default);
}

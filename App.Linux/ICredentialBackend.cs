namespace Coder.Desktop.App.Services;

// These interfaces mirror those in the App project. They are defined here
// because App/ still targets WinUI 3 (net8.0-windows). When the Avalonia
// app is created, these will be replaced with references to the shared
// interfaces.

public class RawCredentials
{
    public string? CoderUrl { get; set; }
    public string? ApiToken { get; set; }
}

public interface ICredentialBackend
{
    Task<RawCredentials?> ReadCredentials(CancellationToken ct = default);
    Task WriteCredentials(RawCredentials credentials, CancellationToken ct = default);
    Task DeleteCredentials(CancellationToken ct = default);
}

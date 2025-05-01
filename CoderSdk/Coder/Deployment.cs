namespace Coder.Desktop.CoderSdk.Coder;

public partial interface ICoderApiClient
{
    public Task<BuildInfo> GetBuildInfo(CancellationToken ct = default);
}

public class BuildInfo
{
    public string Version { get; set; } = "";
}

public partial class CoderApiClient
{
    public Task<BuildInfo> GetBuildInfo(CancellationToken ct = default)
    {
        return SendRequestNoBodyAsync<BuildInfo>(HttpMethod.Get, "/api/v2/buildinfo", ct);
    }
}

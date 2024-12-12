namespace CoderSdk;

public class BuildInfo
{
    public string ExternalUrl { get; set; } = "";
    public string Version { get; set; } = "";
    public string DashboardUrl { get; set; } = "";
    public bool Telemetry { get; set; } = false;
    public bool WorkspaceProxy { get; set; } = false;
    public string AgentApiVersion { get; set; } = "";
    public string ProvisionerApiVersion { get; set; } = "";
    public string UpgradeMessage { get; set; } = "";
    public string DeploymentId { get; set; } = "";
}

public partial class CoderApiClient
{
    public Task<BuildInfo> GetBuildInfo(CancellationToken ct = default)
    {
        return SendRequestAsync<BuildInfo>(HttpMethod.Get, "/api/v2/buildinfo", null, ct);
    }
}

namespace Coder.Desktop.CoderSdk.Coder;

public partial interface ICoderApiClient
{
    public Task<WorkspaceAgent> GetWorkspaceAgent(string id, CancellationToken ct = default);
}

public class WorkspaceAgent
{
    public WorkspaceApp[] Apps { get; set; } = [];
}

public class WorkspaceApp
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool External { get; set; } = false;
    public string DisplayName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

public partial class CoderApiClient
{
    public Task<WorkspaceAgent> GetWorkspaceAgent(string id, CancellationToken ct = default)
    {
        return SendRequestNoBodyAsync<WorkspaceAgent>(HttpMethod.Get, "/api/v2/workspaceagents/" + id, ct);
    }
}

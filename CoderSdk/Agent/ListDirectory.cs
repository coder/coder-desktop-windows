namespace Coder.Desktop.CoderSdk.Agent;

public partial interface IAgentApiClient
{
    public Task<ListDirectoryResponse> ListDirectory(ListDirectoryRequest req, CancellationToken ct = default);
}

public enum ListDirectoryRelativity
{
    // Root means `/` on Linux, and lists drive letters on Windows.
    Root,

    // Home means the user's home directory, usually `/home/xyz` or
    // `C:\Users\xyz`.
    Home,
}

public class ListDirectoryRequest
{
    // Path segments like ["home", "coder", "repo"] or even just []
    public List<string> Path { get; set; } = [];

    // Where the path originates, either in the home directory or on the root
    // of the system
    public ListDirectoryRelativity Relativity { get; set; } = ListDirectoryRelativity.Root;
}

public class ListDirectoryItem
{
    public required string Name { get; init; }
    public required string AbsolutePathString { get; init; }
    public required bool IsDir { get; init; }
}

public class ListDirectoryResponse
{
    // The resolved absolute path (always from root) for future requests.
    // E.g. if you did a request like `home: ["repo"]`,
    // this would return ["home", "coder", "repo"] and "/home/coder/repo"
    public required List<string> AbsolutePath { get; init; }

    // e.g. "C:\\Users\\coder\\repo" or "/home/coder/repo"
    public required string AbsolutePathString { get; init; }
    public required List<ListDirectoryItem> Contents { get; init; }
}

public partial class AgentApiClient
{
    public Task<ListDirectoryResponse> ListDirectory(ListDirectoryRequest req, CancellationToken ct = default)
    {
        return SendRequestAsync<ListDirectoryRequest, ListDirectoryResponse>(HttpMethod.Post, "/api/v0/list-directory",
            req, ct);
    }
}

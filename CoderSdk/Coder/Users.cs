namespace Coder.Desktop.CoderSdk.Coder;

public partial interface ICoderApiClient
{
    public Task<User> GetUser(string user, CancellationToken ct = default);
}

public class User
{
    public const string Me = "me";

    public string Username { get; set; } = "";
}

public partial class CoderApiClient
{
    public Task<User> GetUser(string user, CancellationToken ct = default)
    {
        return SendRequestNoBodyAsync<User>(HttpMethod.Get, $"/api/v2/users/{user}", ct);
    }
}

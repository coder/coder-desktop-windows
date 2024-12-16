namespace CoderSdk;

public class User
{
    public const string Me = "me";

    // TODO: fill out more fields
    public string Username { get; set; } = "";
}

public partial class CoderApiClient
{
    public Task<User> GetUser(string user, CancellationToken ct = default)
    {
        return SendRequestAsync<User>(HttpMethod.Get, $"/api/v2/users/{user}", null, ct);
    }
}

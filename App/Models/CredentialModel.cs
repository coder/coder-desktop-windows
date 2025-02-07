namespace Coder.Desktop.App.Models;

public enum CredentialState
{
    Invalid,
    Valid,
}

public class CredentialModel
{
    public CredentialState State { get; set; } = CredentialState.Invalid;

    public string? CoderUrl { get; set; }
    public string? ApiToken { get; set; }

    public CredentialModel Clone()
    {
        return new CredentialModel
        {
            State = State,
            CoderUrl = CoderUrl,
            ApiToken = ApiToken,
        };
    }
}

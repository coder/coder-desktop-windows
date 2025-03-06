namespace Coder.Desktop.App.Models;

public enum CredentialState
{
    // Unknown means "we haven't checked yet"
    Unknown,

    // Invalid means "we checked and there's either no saved credentials or they are not valid"
    Invalid,

    // Valid means "we checked and there are saved credentials and they are valid"
    Valid,
}

public class CredentialModel
{
    public CredentialState State { get; set; } = CredentialState.Unknown;

    public string? CoderUrl { get; set; }
    public string? ApiToken { get; set; }

    public string? Username { get; set; }

    public CredentialModel Clone()
    {
        return new CredentialModel
        {
            State = State,
            CoderUrl = CoderUrl,
            ApiToken = ApiToken,
            Username = Username,
        };
    }
}

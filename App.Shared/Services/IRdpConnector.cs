namespace Coder.Desktop.App.Services;

public struct RdpCredentials(string username, string password)
{
    public readonly string Username = username;
    public readonly string Password = password;
}

public interface IRdpConnector
{
    const int DefaultPort = 3389;
    void WriteCredentials(string fqdn, RdpCredentials credentials);
    Task Connect(string fqdn, int port = DefaultPort, CancellationToken ct = default);
}

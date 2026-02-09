namespace Coder.Desktop.App.Services;

public class RdpCredentials
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public interface IRdpConnector
{
    const int DefaultPort = 3389;
    void WriteCredentials(string fqdn, RdpCredentials credentials);
    Task Connect(string fqdn, int port = DefaultPort, CancellationToken ct = default);
}

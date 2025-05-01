using Coder.Desktop.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Coder.Desktop.Tests.App.Services;

[TestFixture]
public class RdpConnectorTest
{
    [Test(Description = "Spawns RDP for real")]
    [Ignore("Comment out to run manually")]
    [CancelAfter(30_000)]
    public async Task ConnectToRdp()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSerilog();
        builder.Services.AddSingleton<IRdpConnector, RdpConnector>();
        var services = builder.Services.BuildServiceProvider();

        var rdpConnector = (RdpConnector)services.GetService<IRdpConnector>()!;
        var creds = new RdpCredentials("Administrator", "coderRDP!");
        var workspace = "myworkspace.coder";
        await rdpConnector.WriteCredentials(workspace, creds);
        await rdpConnector.Connect(workspace);
    }
}

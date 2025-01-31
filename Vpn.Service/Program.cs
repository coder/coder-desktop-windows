using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using Serilog;

namespace Coder.Desktop.Vpn.Service;

public static class Program
{
#if DEBUG
    private const string serviceName = "Coder Desktop (Debug)";
#else
    const string serviceName = "Coder Desktop";
#endif

    private static readonly ILogger MainLogger = Log.ForContext("SourceContext", "Coder.Desktop.Vpn.Service.Program");

    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog.
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            // TODO: configurable level
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
            // TODO: better location
            .WriteTo.File(@"C:\CoderDesktop.log",
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            await BuildAndRun(args);
            return 0;
        }
        catch (Exception ex)
        {
            MainLogger.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task BuildAndRun(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configuration sources
        builder.Configuration.Sources.Clear();
        (builder.Configuration as IConfigurationBuilder).Add(
            new RegistryConfigurationSource(Registry.LocalMachine, @"SOFTWARE\Coder\Coder Desktop"));
        builder.Configuration.AddEnvironmentVariables("CODER_MANAGER_");
        builder.Configuration.AddCommandLine(args);

        // Options types (these get registered as IOptions<T> singletons)
        builder.Services.AddOptions<ManagerConfig>()
            .Bind(builder.Configuration.GetSection("Manager"))
            .ValidateDataAnnotations();

        // Logging
        builder.Services.AddSerilog();

        // Singletons
        builder.Services.AddSingleton<IDownloader, Downloader>();
        builder.Services.AddSingleton<ITunnelSupervisor, TunnelSupervisor>();
        builder.Services.AddSingleton<IManager, Manager>();

        // Services
        // TODO: is this sound enough to determine if we're a service?
        if (!Environment.UserInteractive)
        {
            MainLogger.Information("Running as a windows service");
            builder.Services.AddWindowsService(options => { options.ServiceName = serviceName; });
        }
        else
        {
            MainLogger.Information("Running as a console application");
        }

        builder.Services.AddHostedService<ManagerService>();
        builder.Services.AddHostedService<ManagerRpcService>();

        await builder.Build().RunAsync();
    }
}

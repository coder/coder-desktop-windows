using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Coder.Desktop.Vpn.Service;

public static class Program
{
    // These values are the service name and the prefix on registry value names.
    // They should not be changed without backwards compatibility
    // considerations. If changed here, they should also be changed in the
    // installer.
#if !DEBUG
    private const string ServiceName = "Coder Desktop";
    private const string ConfigSubKey = @"SOFTWARE\Coder Desktop\VpnService";
#else
    // This value matches Create-Service.ps1.
    private const string ServiceName = "Coder Desktop (Debug)";
    private const string ConfigSubKey = @"SOFTWARE\Coder Desktop\DebugVpnService";
#endif

    private const string ManagerConfigSection = "Manager";

    private static ILogger MainLogger => Log.ForContext("SourceContext", "Coder.Desktop.Vpn.Service.Program");

    public static async Task<int> Main(string[] args)
    {
        // This logger will only be used until we load our full logging configuration and replace it.
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console()
            .CreateLogger();
        MainLogger.Information("Application is starting");
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
            MainLogger.Information("Application is shutting down");
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task BuildAndRun(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var configBuilder = builder.Configuration as IConfigurationBuilder;

        // Configuration sources
        builder.Configuration.Sources.Clear();
        AddDefaultConfig(configBuilder);
        configBuilder.Add(
            new RegistryConfigurationSource(Registry.LocalMachine, ConfigSubKey));
        builder.Configuration.AddEnvironmentVariables("CODER_MANAGER_");
        builder.Configuration.AddCommandLine(args);

        // Options types (these get registered as IOptions<T> singletons)
        builder.Services.AddOptions<ManagerConfig>()
            .Bind(builder.Configuration.GetSection(ManagerConfigSection))
            .ValidateDataAnnotations();

        // Logging
        builder.Services.AddSerilog((_, loggerConfig) =>
        {
            loggerConfig.ReadFrom.Configuration(builder.Configuration);
        });

        // Singletons
        builder.Services.AddSingleton<IDownloader, Downloader>();
        builder.Services.AddSingleton<ITunnelSupervisor, TunnelSupervisor>();
        builder.Services.AddSingleton<IManagerRpc, ManagerRpc>();
        builder.Services.AddSingleton<IManager, Manager>();
        builder.Services.AddSingleton<ITelemetryEnricher, TelemetryEnricher>();

        // Services
        if (!Environment.UserInteractive)
        {
            MainLogger.Information("Running as a windows service");
            builder.Services.AddWindowsService(options => { options.ServiceName = ServiceName; });
        }
        else
        {
            MainLogger.Information("Running as a console application");
        }

        builder.Services.AddHostedService<ManagerService>();
        builder.Services.AddHostedService<ManagerRpcService>();

        var host = builder.Build();
        Log.Logger = (ILogger)host.Services.GetService(typeof(ILogger))!;
        MainLogger.Information("Application is starting");

        await host.RunAsync();
    }

    private static void AddDefaultConfig(IConfigurationBuilder builder)
    {
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:Using:0"] = "Serilog.Sinks.File",
            ["Serilog:Using:1"] = "Serilog.Sinks.Console",

            ["Serilog:MinimumLevel"] = "Information",
            ["Serilog:Enrich:0"] = "FromLogContext",

            ["Serilog:WriteTo:0:Name"] = "File",
            ["Serilog:WriteTo:0:Args:path"] = @"C:\coder-desktop-service.log",
            ["Serilog:WriteTo:0:Args:outputTemplate"] =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
            ["Serilog:WriteTo:0:Args:rollingInterval"] = "Day",

            ["Serilog:WriteTo:1:Name"] = "Console",
            ["Serilog:WriteTo:1:Args:outputTemplate"] =
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
        });
    }
}

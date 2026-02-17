using Coder.Desktop.Vpn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Coder.Desktop.Vpn.Service;

public static class Program
{
#if !DEBUG
    private const string ServiceName = "Coder Desktop";
    private const string DefaultLogLevel = "Information";
#else
    private const string ServiceName = "Coder Desktop (Debug)";
    private const string DefaultLogLevel = "Debug";
#endif

    private const string ManagerConfigSection = "Manager";

    private static ILogger MainLogger => Log.ForContext("SourceContext", "Coder.Desktop.Vpn.Service.Program");

    public static async Task<int> Main(string[] args)
    {
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
        AddPlatformConfig(configBuilder);
        builder.Configuration.AddEnvironmentVariables("CODER_MANAGER_");
        builder.Configuration.AddCommandLine(args);

        // Options types
        builder.Services.AddOptions<ManagerConfig>()
            .Bind(builder.Configuration.GetSection(ManagerConfigSection))
            .ValidateDataAnnotations();

        // Logging
        builder.Services.AddSerilog((_, loggerConfig) =>
        {
            loggerConfig.ReadFrom.Configuration(builder.Configuration);
        });

        // Platform-specific services
        RegisterPlatformServices(builder);

        // Singletons
        builder.Services.AddSingleton<IDownloader, Downloader>();
        builder.Services.AddSingleton<ITunnelSupervisor, TunnelSupervisor>();
        builder.Services.AddSingleton<IManagerRpc, ManagerRpc>();
        builder.Services.AddSingleton<IManager, Manager>();
        builder.Services.AddSingleton<ITelemetryEnricher, TelemetryEnricher>();

        // Services
        builder.Services.AddHostedService<ManagerService>();
        builder.Services.AddHostedService<ManagerRpcService>();

        var host = builder.Build();
        Log.Logger = (ILogger)host.Services.GetService(typeof(ILogger))!;
        MainLogger.Information("Application is starting");

        await host.RunAsync();
    }

    private static void AddPlatformConfig(IConfigurationBuilder builder)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
#if !DEBUG
            const string configSubKey = @"SOFTWARE\Coder Desktop\VpnService";
#else
            const string configSubKey = @"SOFTWARE\Coder Desktop\DebugVpnService";
#endif
            builder.Add(new RegistryConfigurationSource(
                Microsoft.Win32.Registry.LocalMachine, configSubKey));
        }
#else
        if (OperatingSystem.IsLinux())
        {
            builder.AddJsonFile("/etc/coder-desktop/config.json", optional: true, reloadOnChange: false);
        }
#endif
    }

    private static void RegisterPlatformServices(HostApplicationBuilder builder)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            if (!Environment.UserInteractive)
            {
                MainLogger.Information("Running as a Windows service");
                builder.Services.AddWindowsService(options => { options.ServiceName = ServiceName; });
            }
            else
            {
                MainLogger.Information("Running as a console application");
            }

            // Register Windows named pipe transport
            builder.Services.AddSingleton<IRpcServerTransport>(sp =>
            {
                var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ManagerConfig>>().Value;
                return new NamedPipeServerTransport(config.ServiceRpcPipeName);
            });
        }
#else
#pragma warning disable CA1416 // Platform compatibility - guarded by OperatingSystem.IsLinux() at runtime
        if (OperatingSystem.IsLinux())
        {
            MainLogger.Information("Running as a systemd service");
            builder.Services.AddSystemd();

            // Register Unix socket transport
            builder.Services.AddSingleton<IRpcServerTransport>(sp =>
            {
                var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ManagerConfig>>().Value;
                var socketPath = string.IsNullOrEmpty(config.ServiceRpcSocketPath)
                    ? "/run/coder-desktop/vpn.sock"
                    : config.ServiceRpcSocketPath;
                return new UnixSocketServerTransport(socketPath);
            });
        }
#pragma warning restore CA1416
#endif
    }

    private static void AddDefaultConfig(IConfigurationBuilder builder)
    {
        var logPath = OperatingSystem.IsWindows()
            ? @"C:\coder-desktop-service.log"
            : "/var/log/coder-desktop-service.log";

        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:Using:0"] = "Serilog.Sinks.File",
            ["Serilog:Using:1"] = "Serilog.Sinks.Console",

            ["Serilog:MinimumLevel"] = DefaultLogLevel,
            ["Serilog:Enrich:0"] = "FromLogContext",

            ["Serilog:WriteTo:0:Name"] = "File",
            ["Serilog:WriteTo:0:Args:path"] = logPath,
            ["Serilog:WriteTo:0:Args:outputTemplate"] =
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
            ["Serilog:WriteTo:0:Args:rollingInterval"] = "Day",

            ["Serilog:WriteTo:1:Name"] = "Console",
            ["Serilog:WriteTo:1:Args:outputTemplate"] =
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
        });
    }
}

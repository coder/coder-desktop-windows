using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using Serilog;

namespace Coder.Desktop.Vpn.Service;

public static class Program
{
#if !DEBUG
    private const string ServiceName = "Coder Desktop";
#else
    private const string ServiceName = "Coder Desktop (Debug)";
#endif

    private const string ConsoleOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}";

    private const string FileOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}";

    private static ILogger MainLogger => Log.ForContext("SourceContext", "Coder.Desktop.Vpn.Service.Program");

    private static LoggerConfiguration BaseLogConfig => new LoggerConfiguration()
        .Enrich.FromLogContext()
        .MinimumLevel.Debug()
        .WriteTo.Console(outputTemplate: ConsoleOutputTemplate);

    public static async Task<int> Main(string[] args)
    {
        Log.Logger = BaseLogConfig.CreateLogger();
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

        // Configuration sources
        builder.Configuration.Sources.Clear();
        (builder.Configuration as IConfigurationBuilder).Add(
            new RegistryConfigurationSource(Registry.LocalMachine, @"SOFTWARE\Coder Desktop"));
        builder.Configuration.AddEnvironmentVariables("CODER_MANAGER_");
        builder.Configuration.AddCommandLine(args);

        // Options types (these get registered as IOptions<T> singletons)
        builder.Services.AddOptions<ManagerConfig>()
            .Bind(builder.Configuration.GetSection("Manager"))
            .ValidateDataAnnotations()
            .PostConfigure(config =>
            {
                Log.Logger = BaseLogConfig
                    .WriteTo.File(config.LogFileLocation, outputTemplate: FileOutputTemplate)
                    .CreateLogger();
            });

        // Logging
        builder.Services.AddSerilog();

        // Singletons
        builder.Services.AddSingleton<IDownloader, Downloader>();
        builder.Services.AddSingleton<ITunnelSupervisor, TunnelSupervisor>();
        builder.Services.AddSingleton<IManagerRpc, ManagerRpc>();
        builder.Services.AddSingleton<IManager, Manager>();

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

        await builder.Build().RunAsync();
    }
}

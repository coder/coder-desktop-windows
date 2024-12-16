using Coder.Desktop.Vpn.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

var builder = Host.CreateApplicationBuilder(args);

// Configuration sources
builder.Configuration.Sources.Clear();
(builder.Configuration as IConfigurationBuilder).Add(
    new RegistryConfigurationSource(Registry.LocalMachine, @"SOFTWARE\Coder\Coder VPN"));
builder.Configuration.AddEnvironmentVariables("CODER_MANAGER_");
builder.Configuration.AddCommandLine(args);

// Options types (these get registered as IOptions<T> singletons)
builder.Services.AddOptions<ManagerConfig>()
    .Bind(builder.Configuration.GetSection("Manager"))
    .ValidateDataAnnotations();

// Singletons
builder.Services.AddSingleton<IDownloader, Downloader>();
builder.Services.AddSingleton<ITunnelSupervisor, TunnelSupervisor>();
builder.Services.AddSingleton<IManager, Manager>();

// Services
builder.Services.AddHostedService<ManagerService>();
builder.Services.AddHostedService<ManagerRpcService>();

builder.Build().Run();

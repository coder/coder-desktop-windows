using Coder.Desktop.Vpn.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IDownloader, Downloader>();
builder.Services.AddSingleton<IManager, Manager>();
builder.Services.AddHostedService<ManagerService>();
builder.Services.AddHostedService<ManagerRpcService>();
builder.Build().Run();

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scrobbler;
using Scrobbler.DBus;

ConfigFileHelper.EnsureDefaultConfig();

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(ConfigFileHelper.ConfigPath, optional: true, reloadOnChange: true);

builder.Services.Configure<ScrobblerConfig>(
    builder.Configuration.GetSection("Scrobbler"));

builder.Services.AddSystemd();

builder.Services.AddSingleton<MprisPlayerMonitor>();
builder.Services.AddSingleton<ScrobblingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScrobblingService>());
builder.Services.AddSingleton<ScrobblerDaemonObject>();
builder.Services.AddHostedService<DBusHostingService>();

var host = builder
    .Build();
await host.RunAsync();

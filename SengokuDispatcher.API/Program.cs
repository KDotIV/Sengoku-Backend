using SengokuDispatcher.API;
using SengokuDispatcher.Library.Configurations;
using SengokuDispatcher.Library.DiscordServices;
using SengokuDispatcher.Library.Handlers;
using SengokuDispatcher.Library.Services.Scheduling;
using Microsoft.AspNetCore.DataProtection;
using SengokuDispatcher.Library.Services.Remote;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ");
var connectionString = builder.Configuration.GetConnectionString("AlexandriaConnectionString")
    ?? throw new InvalidOperationException("Alexandria database connection is required.");

var protection = builder.Services.AddDataProtection().SetApplicationName("Sengoku.Accounts");
var keyPath = builder.Configuration["Authentication:DataProtectionKeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.AddDispatcher();
builder.AddEventFeeds();
builder.Services.AddHostedService<HostedServiceDriver<EventFeedWorker>>();
if (builder.Configuration.GetValue<bool>("Dispatcher:ServiceBusEnabled"))
{
    builder.Services.AddHostedService<HostedServiceDriver<EventReceivedWorker>>();
    builder.Services.AddHostedService<HostedServiceDriver<PlayerReceivedWorker>>();
    builder.Services.AddHostedService<HostedServiceDriver<LegendReceivedWorker>>();
    builder.Services.AddHostedService<HostedServiceDriver<BracketOutboxWorker>>();
}
if (builder.Configuration.GetValue<bool>("Dispatcher:SchedulingEnabled"))
{
    builder.Services.AddHostedService<HostedServiceDriver<IntakeScheduleRunner>>();
}
if (builder.Configuration.GetValue<bool>("Dispatcher:DiscordEnabled"))
{
    builder.Services.AddHostedService<HostedServiceDriver<DiscordStartupService>>();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseMiddleware<DispatcherAccessMiddleware>();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { service = "SengokuDispatcher" }));
app.Run();

using SengokuDispatcher.API;
using SengokuDispatcher.Library.Configurations;
using SengokuDispatcher.Library.DiscordServices;
using SengokuDispatcher.Library.Handlers;
using SengokuDispatcher.Library.Services.Scheduling;
using Microsoft.AspNetCore.DataProtection;
using SengokuDispatcher.Library.Services.Remote;
using Sengoku.Observability;
using SengokuDispatcher.Library.Services.Diagnostics;


namespace SengokuDispatcher.API;

public static class DispatcherApplication
{
    public static WebApplication Build(WebApplicationBuilder builder, Action<IServiceCollection>? customize = null)
    {
        builder.Services.AddOperationLogging();
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

        builder.Services.AddControllers().AddApplicationPart(typeof(Controllers.CommunicationController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.InstrumentOperations(typeof(SengokuProvider.Library.Services.Users.IUserService).Assembly,
            typeof(SengokuDispatcher.Library.Workflows.Events.IEventOperations).Assembly);
        customize?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<OperationMiddleware>();
        app.UseMiddleware<DispatcherAccessMiddleware>();
        app.MapControllers();
        app.MapGet("/health/live", () => Results.Ok(new { service = "SengokuDispatcher" }));
        app.MapGet("/internal/health/ready", async (DispatcherReadiness readiness, CancellationToken ct) =>
        {
            var report = await readiness.Check(ct);
            return Results.Json(report, statusCode: report.Status == "ready" ? 200 : 503);
        });
        return app;
    }
}

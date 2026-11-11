using SengokuDispatcher.Library.Configurations;
using SengokuProvider.Library.Configurations;
using Sengoku.Observability;
using SengokuProvider.API;


namespace SengokuProvider.API;

public static class ProviderApplication
{
    public static WebApplication Build(WebApplicationBuilder builder, Action<IServiceCollection>? customize = null)
    {
        builder.Services.AddOperationLogging();
        builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ");
        var connectionString = builder.Configuration.GetConnectionString("AlexandriaConnectionString")
            ?? throw new InvalidOperationException("Alexandria database connection is required.");
        builder.AddAccountAuthentication(connectionString);
        builder.AddDispatcher(backgroundHost: false);
        // Provider manages feed definitions/subscriptions; Dispatcher alone performs delivery.
        builder.AddEventFeeds(backgroundHost: false);
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(Controllers.EventFeedsController).Assembly);
        builder.Services.AddHttpClient();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.InstrumentOperations(typeof(SengokuProvider.Library.Services.Users.IUserService).Assembly,
            typeof(SengokuDispatcher.Library.Workflows.Events.IEventOperations).Assembly);
        customize?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<OperationMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors("AccountFrontend");
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        app.MapProviderReadiness();
        return app;
    }
}

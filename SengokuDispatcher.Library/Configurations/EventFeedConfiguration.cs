using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuProvider.Library.Services.Events;

namespace SengokuDispatcher.Library.Configurations;

public static class EventFeedConfiguration
{
    public static void AddEventFeeds(this WebApplicationBuilder builder, bool backgroundHost = true)
    {
        var connectionString = builder.Configuration.GetConnectionString("AlexandriaConnectionString")
            ?? throw new InvalidOperationException("Alexandria database connection is required.");
        builder.Services.AddScoped(sp => new EventFeedStore(connectionString, sp.GetRequiredService<IDataProtectionProvider>(), sp.GetRequiredService<ILogger<EventFeedStore>>()));
        builder.Services.AddScoped<IEventFeedStore>(sp => sp.GetRequiredService<EventFeedStore>());
        if (backgroundHost) builder.Services.AddScoped<IEventFeedDispatcher>(sp => sp.GetRequiredService<EventFeedStore>());
        builder.Services.AddScoped<IDiscordFeedConnectionStore>(sp => sp.GetRequiredService<EventFeedStore>());
        if (backgroundHost)
        {
            builder.Services.AddScoped<IDiscordFeedConnection, DiscordFeedConnection>();
            builder.Services.AddScoped<IDiscordWebhookConnection, DiscordWebhookConnection>();
        }
        else
        {
            builder.Services.AddScoped<IDiscordFeedConnection, SengokuDispatcher.Library.Services.Remote.RemoteDiscordFeedConnection>();
            builder.Services.AddScoped<IDiscordWebhookConnection, SengokuDispatcher.Library.Services.Remote.RemoteDiscordWebhookConnection>();
        }
        builder.Services.AddScoped<IEventFeedQueryService>(sp => sp.GetRequiredService<EventQueryService>());
        if (backgroundHost)
        {
            var commsEnabled = builder.Configuration.GetValue<bool>("EventFeeds:CommsDeliverablesEnabled");
            if (builder.Configuration.GetValue<bool>("EventFeeds:DeliveryEnabled") && !commsEnabled)
                throw new InvalidOperationException("Feed delivery requires EventFeeds:CommsDeliverablesEnabled.");
            if (commsEnabled)
            {
                if (!builder.Configuration.GetValue<bool>("Dispatcher:ServiceBusEnabled"))
                    throw new InvalidOperationException("Calendar refresh requires Dispatcher:ServiceBusEnabled.");
                var commsConnection = builder.Configuration["ServiceBusSettings:CommsDeliverablesConnection"];
                if (string.IsNullOrWhiteSpace(commsConnection))
                    throw new InvalidOperationException("Configure ServiceBusSettings:CommsDeliverablesConnection.");
                builder.Services.AddKeyedSingleton<Azure.Messaging.ServiceBus.ServiceBusClient>("comms",
                    (sp, key) => new Azure.Messaging.ServiceBus.ServiceBusClient(commsConnection));
                builder.Services.AddHostedService<SengokuDispatcher.Library.Handlers.CommsDeliverablesWorker>();
                builder.Services.AddHostedService<SengokuDispatcher.Library.Handlers.MajorCalendarRefreshWorker>();
            }
            builder.Services.AddScoped<SengokuDispatcher.Library.Services.Diagnostics.DispatcherReadiness>();
            builder.Services.AddHttpClient("DiscordFeedOAuth", client => client.Timeout = TimeSpan.FromSeconds(20))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();

            builder.Services.AddHttpClient<IEventFeedOutlet, DiscordEventFeedOutlet>(client => client.Timeout = TimeSpan.FromSeconds(20))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                .RemoveAllLoggers();


        }
    }
}




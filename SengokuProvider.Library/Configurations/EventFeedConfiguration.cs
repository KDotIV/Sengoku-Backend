using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SengokuProvider.Library.Services.Feeds;
using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Library.Configurations;

public static class EventFeedConfiguration
{
    public static void AddEventFeeds<TWorker>(this WebApplicationBuilder builder, string connectionString) where TWorker : class, IHostedService
    {
        builder.Services.AddScoped(sp => new EventFeedStore(connectionString, sp.GetRequiredService<IDataProtectionProvider>()));
        builder.Services.AddScoped<IEventFeedStore>(sp => sp.GetRequiredService<EventFeedStore>());
        builder.Services.AddScoped<IEventFeedDispatcher>(sp => sp.GetRequiredService<EventFeedStore>());
        builder.Services.AddScoped<IDiscordFeedConnectionStore>(sp => sp.GetRequiredService<EventFeedStore>());
        builder.Services.AddScoped<IDiscordFeedConnection, DiscordFeedConnection>();
        builder.Services.AddScoped<IDiscordWebhookConnection, DiscordWebhookConnection>();
        builder.Services.AddScoped<IEventFeedQueryService>(sp => (IEventFeedQueryService)sp.GetRequiredService<IEventQueryService>());
        builder.Services.AddHttpClient("DiscordFeedOAuth", client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();

        builder.Services.AddHttpClient<IEventFeedOutlet, DiscordEventFeedOutlet>(client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();

        builder.Services.AddHostedService<TWorker>();
    }
}




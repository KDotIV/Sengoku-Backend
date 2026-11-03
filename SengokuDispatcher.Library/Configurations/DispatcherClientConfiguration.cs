using Microsoft.AspNetCore.Builder;
using SengokuDispatcher.Library.Services.Common.Interfaces;
using SengokuDispatcher.Library.Services.Comms;
using SengokuDispatcher.Library.Services.Remote;

namespace SengokuDispatcher.Library.Configurations;

public static class DispatcherClientConfiguration
{
    public static void AddDispatcherClient(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient<DispatcherClient>(client => client.Timeout = TimeSpan.FromSeconds(90))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        builder.Services.AddScoped<IAzureBusApiService, RemoteBusApiService>();
        builder.Services.AddScoped<IDiscordWebhookHandler, RemoteDiscordWebhookHandler>();
    }
}

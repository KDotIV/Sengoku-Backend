using SengokuDispatcher.Library.Services.Feeds;
using Microsoft.AspNetCore.DataProtection;
using SengokuProvider.Library.Services.Events;

namespace SengokuDispatcher.Library.Handlers;

public sealed class EventFeedWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<EventFeedWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Opt-in until the additive migration and shared persistent data-protection keys are deployed.
        if (!config.GetValue<bool>("EventFeeds:DeliveryEnabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IEventFeedDispatcher>();
                var query = scope.ServiceProvider.GetRequiredService<IEventFeedQueryService>();
                var outlet = scope.ServiceProvider.GetRequiredService<IEventFeedOutlet>();
                await store.DiscoverNewTournaments(stoppingToken);
                await store.RecoverInterrupted(stoppingToken);
                for (var i = 0; i < 20 && !stoppingToken.IsCancellationRequested; i++)
                {
                    if (!await store.PrepareNext(query, stoppingToken)) break;
                }
                for (var i = 0; i < 20 && !stoppingToken.IsCancellationRequested; i++)
                {
                    if (!await store.DispatchNext(outlet, stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // Never log transport exceptions that might contain a webhook token or SQL parameters.
                log.LogError("Event feed processing failed ({ErrorType}). It will be checked again next poll.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}



using SengokuDispatcher.Library.Services.Feeds;
using Microsoft.AspNetCore.DataProtection;
using SengokuProvider.Library.Services.Events;
using Sengoku.Observability;
using SengokuDispatcher.Library.Services.Diagnostics;

namespace SengokuDispatcher.Library.Handlers;

public sealed class EventFeedWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<EventFeedWorker> log, OperationLogger operations, WorkerActivity activity) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only Dispatcher owns delivery; local integration tests use an isolated database.
        if (!config.GetValue<bool>("EventFeeds:DeliveryEnabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var operation = operations.Begin("EventFeeds.Cycle");
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IEventFeedDispatcher>();
                var query = scope.ServiceProvider.GetRequiredService<IEventFeedQueryService>();
                var outlet = scope.ServiceProvider.GetRequiredService<IEventFeedOutlet>();
                await store.DiscoverNewTournaments(stoppingToken);
                await store.RecoverInterrupted(stoppingToken);
                activity.Success("feeds");
                for (var i = 0; i < 20 && !stoppingToken.IsCancellationRequested; i++)
                {
                    if (!await store.PrepareNext(query, stoppingToken)) break;
                    activity.Success("feeds");
                }
                for (var i = 0; i < 20 && !stoppingToken.IsCancellationRequested; i++)
                {
                    if (!await store.DispatchNext(outlet, stoppingToken)) break;
                    activity.Success("feeds");
                }
                operation.Complete();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { operation.Cancel(); return; }
            catch (Exception ex)
            {
                activity.Failure("feeds");
                operation.Fail(ex);
                Sengoku.Observability.OperationLog.Failure(ex);
                // Never log transport exceptions that might contain a webhook token or SQL parameters.
                log.LogError("Event feed processing failed ({ErrorType}). It will be checked again next poll.", ex.GetType().Name);
            }
            operation.Dispose();
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}



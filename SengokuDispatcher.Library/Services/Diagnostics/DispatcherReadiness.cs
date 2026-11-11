using System.Reflection;
using System.Security.Cryptography;
using Azure.Messaging.ServiceBus;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SengokuDispatcher.Library.Services.Diagnostics;

public sealed record ReadinessReport(string Status, string Revision, IReadOnlyDictionary<string, bool> Checks);

/// <summary>Inspects dependencies without publishing, claiming jobs, or sending notifications.</summary>
public sealed class DispatcherReadiness(IConfiguration config, IDataProtectionProvider protection,
    WorkerActivity workers, IServiceProvider services)
{
    public static string Revision => typeof(DispatcherReadiness).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+').Last() ?? "unknown";

    public async Task<ReadinessReport> Check(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        var checks = new Dictionary<string, bool>();
        checks["configuration"] = config.GetValue<bool>("EventFeeds:DeliveryEnabled")
            && config.GetValue<bool>("EventFeeds:CommsDeliverablesEnabled")
            && config.GetValue<bool>("Dispatcher:ServiceBusEnabled")
            && config["Dispatcher:ApiKey"] is { Length: >= 32 };
        checks["feedWorker"] = workers.Healthy("feeds");
        checks["outboxWorker"] = workers.Healthy("comms");
        checks["database"] = checks["schema"] = checks["destinationKeys"] = checks["subscriptions"] = false;
        var protector = protection.CreateProtector("Sengoku.EventFeeds.Discord.v1");
        try
        {
            await using var db = new NpgsqlConnection(config.GetConnectionString("AlexandriaConnectionString"));
            await db.OpenAsync(ct);
            checks["database"] = true;
            string[] signatures = ["event_feed_claim_current()", "event_feed_edition_maintenance()",
                "event_feed_next_edition_due()", "event_feed_accept_change(integer,timestamp with time zone)",
                "event_feed_complete(uuid,text,text,integer)", "event_feed_recover()"];
            checks["schema"] = await db.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM unnest(@signatures) signature WHERE to_regprocedure(signature) IS NULL",
                new { signatures }, cancellationToken: ct)) == 0;
            // Compile the columns used by the worker as well as checking its functions.
            await db.ExecuteAsync(new CommandDefinition("""
                SELECT expires_at FROM event_feed_deliveries WHERE false;
                SELECT edition_id,created_at FROM event_feed_edition_work WHERE false;
                SELECT entity,edition_id,occurred_at FROM comms_change_outbox WHERE false;
                """, cancellationToken: ct));
            var destinations = await db.QueryAsync<string>(new CommandDefinition("""
                SELECT destination_protected FROM event_feed_subscriptions WHERE destination_protected IS NOT NULL
                UNION SELECT destination_protected FROM event_feed_deliveries
                    WHERE status IN ('Pending','Sending') AND destination_protected IS NOT NULL
                """, cancellationToken: ct));
            foreach (var destination in destinations) _ = protector.Unprotect(destination);
            checks["destinationKeys"] = protector.Unprotect(protector.Protect("readiness")) == "readiness";
            checks["subscriptions"] = !await db.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT FROM event_feed_subscriptions s
                    JOIN event_feeds f ON f.id=s.feed_id
                    JOIN LATERAL (SELECT status FROM event_feed_deliveries d WHERE d.subscription_id=s.id
                        ORDER BY scheduled_for DESC LIMIT 1) latest ON true
                    WHERE f.enabled AND NOT s.enabled AND latest.status IN ('Failed','Unknown'))
                """, cancellationToken: ct));
        }
        catch (CryptographicException) { checks["destinationKeys"] = false; }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException)
        { checks["schema"] = false; }

        checks["queues"] = false;
        if (checks["configuration"])
        {
            try
            {
                var intake = services.GetRequiredService<ServiceBusClient>();
                foreach (var name in new[] { "EventReceivedQueue", "PlayerReceivedQueue", "LegendReceivedQueue" })
                    await Peek(intake, config["ServiceBusSettings:" + name]!, ct);
                await Peek(services.GetRequiredKeyedService<ServiceBusClient>("comms"), "commsdeliverablesqueue", ct);
                checks["queues"] = true;
            }
            catch (Exception ex) when (ex is ServiceBusException or OperationCanceledException or InvalidOperationException)
            { checks["queues"] = false; }
        }
        return new(checks.Values.All(value => value) ? "ready" : "unready", Revision, checks);
    }

    private static async Task Peek(ServiceBusClient client, string queue, CancellationToken ct)
    {
        await using var receiver = client.CreateReceiver(queue);
        _ = await receiver.PeekMessagesAsync(1, cancellationToken: ct);
    }
}

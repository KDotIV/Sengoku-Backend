using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using SengokuDispatcher.Library.Models.Feeds;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Events;

internal static class TimelineDeliveryChecks
{
    internal static async Task Run(string connection)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await db.ExecuteAsync("INSERT INTO games(id,game_name) VALUES(2,'Second selected game') ON CONFLICT DO NOTHING");
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            var id = 7000 + i;
            await db.ExecuteAsync("""
                INSERT INTO events(id,link_id,event_name,start_time,online_tournament,country_code,addr_state,city,registration_open)
                VALUES(@id,@id,@name,@starts,false,'US','GA','TimelineTown',true);
                INSERT INTO tournament_links(id,event_link,game_id,url_slug,entrants_num,start_time)
                VALUES(@a,@id,1,@slug || '/event/first-game',12,@first),(@b,@id,2,@slug || '/event/second-game',8,@second);
                """, new { id, name = $"Tournament {i} " + new string('x', 120), starts = DateTime.SpecifyKind(now.AddDays(3).AddHours(i), DateTimeKind.Unspecified),
                    a = id * 10, b = id * 10 + 1, slug = "tournament/" + new string('s', 70) + i,
                    first = now.AddDays(3).AddHours(i + 1), second = now.AddDays(3).AddHours(i + 2) });
        }
        var filter = new EventFeedFilter { GameIds = [1, 2], RegionIds = ["southeast"], City = "TimelineTown", MaxEvents = 10 };
        var query = new EventQueryService(connection, null!, null!, null!, null!);
        var one = await query.GetUpcomingFeedEvents(filter with { MaxEvents = 1 }, now);
        Check(one.Events.Count == 1 && one.HasMore && one.Events[0].Brackets.Count == 2, "MaxEvents counts parents, with all selected-game brackets");
        var link = await query.GetUpcomingFeedEvents(filter, now, tournamentIds: [70000]);
        Check(link.Events.Count == 1 && link.Events[0].Brackets.Count == 2, "single bracket intake expands all selected siblings");
        var store = new EventFeedStore(connection, new EphemeralDataProtectionProvider());
        foreach (var mode in new[] { "OnInsert", "Daily", "Interval" })
        {
            var feed = (await store.Save(1, null, new SaveEventFeedRequest { Name = "Timeline budget " + mode, Enabled = true,
                Filter = filter, Schedule = mode switch { "Daily" => new() { Mode = mode, UtcTime = "16:00" },
                    "Interval" => new() { Mode = mode, IntervalHours = 1 }, _ => new() { Mode = mode } } }, default))!;
            var sub = (await store.Subscribe(2, feed.Id, "Discord", default))!;
            await store.ConnectDiscord(2, sub.Id, sub.Version, "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789", "guild", "channel", default);
            var outlet = new Outlet();
            for (var page = 0; page < 12; page++)
            {
                await db.ExecuteAsync("UPDATE event_feed_subscriptions SET next_run_at=now()-interval '1 second' WHERE id=@id", new { sub.Id });
                Check(await store.PrepareNext(query, default), mode + " has a next page");
                Check(await store.DispatchNext(outlet, default), mode + " page sent");
                Check(outlet.Digests.Last().Events.All(e => e.Brackets?.Count == 2), "a parent was split between messages");
                if (outlet.Digests.Sum(d => d.Events.Count) >= 10) break;
                Check(outlet.Digests.Last().DeferredEditionIds?.Length > 0, "remaining parents recorded in cursor");
                Check(await db.ExecuteScalarAsync<bool>("""
                    SELECT destination_protected IS NULL AND payload ? 'DeferredEditionIds' AND NOT payload ? 'Events'
                    FROM event_feed_deliveries WHERE subscription_id=@id ORDER BY scheduled_for DESC LIMIT 1
                    """, new { sub.Id }), "only a non-secret ID cursor survives successful delivery");
                if (mode == "OnInsert")
                    Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_edition_work WHERE subscription_id=@id", new { sub.Id }) > 0, "reconnect wake-up retained for overflow");
                // Move successful sends beyond the five-hour cooldown before requesting another page.
                await db.ExecuteAsync("UPDATE event_feed_deliveries SET completed_at=now()-interval '6 hours' WHERE subscription_id=@id AND status='Sent'", new { sub.Id });
            }
            Check(outlet.Digests.Count > 1 && outlet.Digests.Sum(d => d.Events.Count) == 10 && outlet.Digests.SelectMany(d => d.Events).Select(e => e.Name).Distinct().Count() == 10,
                mode + " delivers every tournament once across full-message pages");
            if (mode == "OnInsert")
            {
                Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_edition_history WHERE subscription_id=@id", new { sub.Id }) == 10, "only fully included parents recorded in history");
                Check(!await store.PrepareNext(query, default), "drained insert queue stops");
            }
            if (mode == "Daily")
            {
                await db.ExecuteAsync("UPDATE event_feed_deliveries SET completed_at=now()-interval '6 hours' WHERE subscription_id=@id AND status='Sent'", new { sub.Id });
                await db.ExecuteAsync("UPDATE event_feed_subscriptions SET next_run_at=now()-interval '1 second' WHERE id=@id", new { sub.Id });
                Check(await store.PrepareNext(query, default) && await store.DispatchNext(outlet, default), "fresh scheduled cycle starts");
                feed = (await store.Save(1, feed.Id, new SaveEventFeedRequest { Name = "Edited daily feed", Enabled = true, Filter = filter,
                    Schedule = new() { Mode = "Daily", UtcTime = "16:00" }, ExpectedVersion = feed.Version }, default))!;
                await db.ExecuteAsync("UPDATE event_feed_deliveries SET completed_at=now()-interval '6 hours' WHERE subscription_id=@id AND status='Sent'", new { sub.Id });
                await db.ExecuteAsync("UPDATE event_feed_subscriptions SET next_run_at=now()-interval '1 second' WHERE id=@id", new { sub.Id });
                Check(await store.PrepareNext(query, default) && await store.DispatchNext(outlet, default), "edited schedule can send");
                Check(outlet.Digests.Last().Events[0].Name == outlet.Digests[0].Events[0].Name, "definition edits invalidate the old overflow cursor");
            }
            await store.Delete(1, feed.Id, default);
        }
        Console.WriteLine("PASS PostgreSQL multi-game expansion and whole-tournament overflow across insert, daily and interval deliveries");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private sealed class Outlet : IEventFeedOutlet
    {
        public string Provider => "Discord";
        public List<EventDigest> Digests { get; } = [];
        public Task<DeliveryResult> Send(string destination, EventDigest digest, CancellationToken ct)
        {
            _ = DiscordEventFeedOutlet.Payload(digest);
            Digests.Add(digest);
            return Task.FromResult(new DeliveryResult("Sent", "local-timeline"));
        }
    }
}

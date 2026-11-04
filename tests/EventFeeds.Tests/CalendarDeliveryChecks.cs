using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using SengokuDispatcher.Library.Models.Feeds;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Events;

internal static class CalendarDeliveryChecks
{
    internal static async Task Run(string connection)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        var store = new EventFeedStore(connection, new EphemeralDataProtectionProvider());
        var query = new EventQueryService(connection, null!, null!, null!, null!);
        var filter = new EventFeedFilter { GameIds = [1], RegionIds = ["southeast"], MaxEvents = 5 };
        var major = new MajorCalendarEdition {
            Name = "Annual championship", GameIds = [1], StartsAt = DateTimeOffset.UtcNow.AddMonths(4),
            RegistrationOpensAt = DateTimeOffset.UtcNow.AddDays(-1), City = "Savannah", State = "GA", Country = "US"
        };
        try { await store.SaveMajor(2, null, major, default); throw new Exception("Calendar allowed non-admin edit"); }
        catch (UnauthorizedAccessException) { }
        var id = await store.SaveMajor(1, null, major, default);
        var entry = (await store.MajorCalendar(default)).Single(e => e.Id == id);
        Check(entry.StartggId == null && entry.GameIds.SequenceEqual([1]), "calendar before bracket publication");
        Check((await query.GetUpcomingFeedEvents(filter, DateTime.UtcNow)).Events.Any(e => e.EditionId == -id), "regional major visible months ahead");
        Check(!(await query.GetUpcomingFeedEvents(filter with { City = "Atlanta" }, DateTime.UtcNow)).Events.Any(e => e.EditionId == -id), "major still respects city filter");
        var feed = (await store.Save(1, null, new SaveEventFeedRequest {
            Name = "Calendar check", Filter = filter, Enabled = true, Schedule = new() { Mode = "OnInsert" }
        }, default))!;
        var sub = (await store.Subscribe(2, feed.Id, "Discord", default))!;
        const string webhook = "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789";
        await store.ConnectDiscord(2, sub.Id, sub.Version, webhook, "guild", "channel", default);
        Check(await store.PrepareNext(query, default), "reconnect snapshot prepared");
        var outlet = new Outlet();
        Check(await store.DispatchNext(outlet, default) && outlet.Digests.Single().Events.Count(e => e.Name == major.Name) == 1, "one unlinked major sent");
        await store.SaveMajor(1, id, entry with { StartggId = 601 }, default);
        await db.ExecuteAsync("""
            INSERT INTO events(id,link_id,event_name,address_id,start_time,online_tournament,country_code,addr_state,registration_open)
            VALUES(601,601,'Linked major',0,timezone('UTC',now())+interval '4 months',false,'US','GA',true),
                  (602,602,'Distant local',0,timezone('UTC',now())+interval '1 month',false,'US','GA',true);
            INSERT INTO tournament_links(id,event_link,game_id,url_slug)
            VALUES(611,601,1,'tournament/annual/event/a'),(612,601,1,'tournament/annual/event/b'),(613,602,1,'tournament/local');
            SELECT event_feed_accept_change(edition_id,occurred_at) FROM comms_change_outbox;
            """);
        var preview = await query.GetUpcomingFeedEvents(filter,DateTime.UtcNow);
        Check(preview.Events.Count(e => e.EditionId == -id) == 1 && preview.Events.All(e => e.EditionId != 602), "two brackets form one edition and local respects horizon");
        while (await store.PrepareNext(query, default)) { }
        Check(!await store.DispatchNext(outlet, default), "no repeat after linking and replay");
        await db.ExecuteAsync("""
            UPDATE events SET start_time=timezone('UTC',now())+interval '14 days'-interval '1 minute' WHERE link_id=602;
            UPDATE event_feed_subscriptions SET boundary_checked_at=now()-interval '2 minutes' WHERE id=@id;
            """,new { id = sub.Id });
        await store.DiscoverNewTournaments(default);
        Check(await store.PrepareNext(query,default) && await store.DispatchNext(outlet, default) &&
            outlet.Digests.Last().Events.Single().Name == "Distant local", "window transition wakes a local without a broker message");
        await db.ExecuteAsync("SELECT event_feed_accept_change(602,now()-interval '8 days')");
        Check(!await store.PrepareNext(query,default), "expired change never creates a notification");
        sub = (await store.GetSubscription(2,sub.Id,default))!;
        await store.ConnectDiscord(2, sub.Id, sub.Version, webhook, "guild", "channel", default);
        Check(await store.PrepareNext(query,default) && !await store.DispatchNext(outlet,default), "reconnect excludes previously sent editions");
        await db.ExecuteAsync("""
            INSERT INTO event_feed_edition_work VALUES(@id,602,now()-interval '8 days');
            """, new { id=sub.Id });
        await store.DiscoverNewTournaments(default);
        Check(!await store.PrepareNext(query,default), "maintenance purges expired queued work");
        foreach (var cadence in new[] { new FeedSchedule {Mode="Daily", UtcTime="16:00"}, new FeedSchedule {Mode="Interval",IntervalHours=168} })
        {
            feed = (await store.Save(1,feed.Id,new SaveEventFeedRequest {
                Name=feed.Name, Filter=filter, Enabled=true, Schedule=cadence, ExpectedVersion=feed.Version
            }, default))!;
            await db.ExecuteAsync("UPDATE event_feed_subscriptions SET next_run_at=now()-interval '1 second' WHERE id=@id", new { id=sub.Id });
            Check(await store.PrepareNext(query,default), "scheduled digest can be prepared without an insert");
            if (cadence.Mode=="Interval")
            {
                await db.ExecuteAsync("UPDATE event_feed_deliveries SET expires_at=now()-interval '1 second' WHERE subscription_id=@id AND status='Pending'",new { id=sub.Id });
                Check(!await store.DispatchNext(outlet, default), "expired retry cannot reach Discord");
            }
            else Check(await store.DispatchNext(outlet,default) && outlet.Digests.Last().Events.Any(e=>e.Name=="Linked major"), "daily snapshot includes major");
        }
        await store.Delete(1,feed.Id,default);
        Console.WriteLine("PASS calendar authorization, pre-bracket matching, reconnect, edition deduplication, rolling horizon and expiry");
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    private sealed class Outlet : IEventFeedOutlet
    {
        public string Provider => "Discord";
        public List<EventDigest> Digests { get; } = [];
        public Task<DeliveryResult> Send(string destination, EventDigest digest, CancellationToken ct)
        {
            Digests.Add(digest);
            return Task.FromResult(new DeliveryResult("Sent","mock"));
        }
    }
}

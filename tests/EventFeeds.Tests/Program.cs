using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.DataProtection;
using SengokuProvider.Library.Services.Feeds;

using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Events;

const string webhook = "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789";
Check(DiscordEventFeedOutlet.ValidateWebhook(webhook) == webhook, "valid webhook");
foreach (var bad in new[] { "http://discord.com/api/webhooks/123/token", "https://localhost/", webhook.Replace("discord.com", "discord.com.evil.test"), webhook + "?thread_id=1", webhook.Replace("discord.com", "discord.com:444"), "https://user@discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789" })
    await Throws<ArgumentException>(() => Task.FromResult(DiscordEventFeedOutlet.ValidateWebhook(bad)));
Check(BroadcastLink.Parse("https://www.twitch.tv/capcomfighters")?.Provider == "Twitch" && BroadcastLink.Parse("https://www.youtube.com/watch?v=abcdefghijk")?.Provider == "YouTube" && BroadcastLink.Parse("javascript:alert(1)") == null, "safe guide broadcast references");

var filter = new EventFeedFilter { GameIds = [1], RegionIds = ["southeast"] };
await Throws<ArgumentException>(() => Task.FromResult((filter with { GameIds = [] }).Normalize()));
await Throws<ArgumentException>(() => Task.FromResult((filter with { RegionIds = ["invalid region"] }).Normalize()));
await Throws<ArgumentException>(() => Task.FromResult((filter with { LookAheadDays = 1000 }).Normalize()));
var digest = new EventDigest("@everyone Test", Enumerable.Range(0,10).Select(i => new DigestEvent(new string('a',300), "tournament/test/event/sf6", DateTime.UtcNow, "Atlanta, GA", 25)).ToArray(), true);
using var payload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(digest)));
Check(payload.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength() == 0, "mentions suppressed");
Check(payload.RootElement.GetProperty("embeds").EnumerateArray().Sum(e => e.GetProperty("title").GetString()!.Length + e.GetProperty("description").GetString()!.Length) < 6000, "Discord aggregate embed budget");
await Throws<ArgumentException>(() => Task.FromResult(DiscordEventFeedOutlet.Payload(digest with {
    Events = digest.Events.Select(e => e with { SignupSlug = "tournament/" + new string('x', 700) }).ToArray()
})));
await Throws<ArgumentException>(() => Task.FromResult(DiscordEventFeedOutlet.SignupUrl("https://evil.test")));
var transport = new FakeHttp();
using var http = new HttpClient(transport);
var discord = new DiscordEventFeedOutlet(http);
Check((await discord.Send(webhook, digest, default)).Status == "Sent", "successful delivery acknowledged");
transport.Status = HttpStatusCode.TooManyRequests; transport.Body = "{\"retry_after\":125.5}";
Check((await discord.Send(webhook, digest, default)).RetryAfterSeconds >= 126, "rate-limit delay honored");
transport.Body = "{\"retry_after\":172800}";
Check((await discord.Send(webhook, digest, default)).RetryAfterSeconds >= 172800, "long rate-limit delays are not shortened");
transport.Status = HttpStatusCode.InternalServerError;
Check((await discord.Send(webhook, digest, default)).Status == "Unknown", "ambiguous server error not blindly retried");
transport.Status = HttpStatusCode.NotFound;
Check((await discord.Send(webhook, digest, default)).Status == "InvalidDestination", "deleted webhook classified");
Console.WriteLine("PASS filter validation, Southeast preset, safe webhook URLs, Discord formatting, acknowledgements and rate limits");
if (args.Length < 1) { Console.WriteLine("SKIP PostgreSQL checks: pass appsettings path to use a disposable test schema."); return; }
using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
var configured = settings.RootElement.GetProperty("ConnectionStrings").GetProperty("AlexandriaConnectionString").GetString()!;
var schema = "feed_tests_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(configured);
await admin.OpenAsync();
if (args.Contains("--inspect"))
{
    foreach (var row in await admin.QueryAsync("SELECT table_name,column_name,data_type FROM information_schema.columns WHERE table_schema='public' AND (table_name ILIKE '%game%' OR table_name='user_sessions') ORDER BY table_name,ordinal_position")) Console.WriteLine(JsonSerializer.Serialize(row));
    return;
}
var geographyColumns = await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='events' AND column_name IN ('city','addr_state','country_code')");
var feedTableExists = await admin.ExecuteScalarAsync<bool>("SELECT to_regclass('public.event_feeds') IS NOT NULL");
Console.WriteLine($"Read-only database preflight: {geographyColumns}/3 event geography columns; event_feeds exists: {feedTableExists}.");
await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
try
{
    var scoped = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema }.ConnectionString;
    await using var conn = new NpgsqlConnection(scoped);
    await conn.OpenAsync();
    await conn.ExecuteAsync("""
        CREATE TABLE users(id int PRIMARY KEY);
        INSERT INTO users VALUES(1),(2);
        CREATE TABLE user_sessions(token_hash text PRIMARY KEY,user_id integer NOT NULL REFERENCES users(id),expires_at timestamptz NOT NULL);
        CREATE TABLE games(id integer PRIMARY KEY,game_name varchar);
        INSERT INTO games VALUES(1,'Street Fighter 6'),(99,'Tekken'),(98765,'Empty game');
        CREATE TABLE events(id serial PRIMARY KEY, link_id int UNIQUE, event_name text, event_description text, address_id integer, url_slug text, last_updated timestamp, region text, start_time timestamp,
            end_time timestamp, closing_registration_date timestamp, registration_open boolean, online_tournament boolean);
        CREATE TABLE tournament_links(id int PRIMARY KEY,event_link int,game_id int,url_slug text,entrants_num int,last_updated timestamp,viewership text[]);
        """);
    var migration = (await File.ReadAllTextAsync("database/migrations/20261013_event_feeds.sql")).Replace("public.", schema + ".");
    await conn.ExecuteAsync(migration);
    await conn.ExecuteAsync(migration);
    var functions = (await File.ReadAllTextAsync("database/migrations/20261013_event_feed_functions.sql")).Replace("public.", schema + ".");
    await conn.ExecuteAsync(functions);
    await conn.ExecuteAsync(functions);
    var uxMigration = (await File.ReadAllTextAsync("database/migrations/20261013_event_feed_integration_ux.sql")).Replace("public.", schema + ".");
    await conn.ExecuteAsync(uxMigration);
    await conn.ExecuteAsync(uxMigration);
    // Exercise intake against the function, including UTC timestamp types and unknown closing dates.
    var intake = new EventIntakeService(scoped);
    var incoming = new SengokuProvider.Library.Models.Events.EventData {
        LinkID = 100, EventName = "Intake check", LastUpdate = DateTime.UtcNow,
        StartTime = DateTime.UtcNow.AddDays(2), City = "Atlanta", AddrState = "ga", CountryCode = "us", IsRegistrationOpen = true
    };
    Check(await intake.InsertNewEventsData([incoming]) == 1, "intake function accepts UTC values");
    incoming.City = null;
    Check(await intake.InsertNewEventsData([incoming]) == 1, "intake upsert");
    Check(await conn.ExecuteScalarAsync<bool>("SELECT city='Atlanta' AND addr_state='GA' AND country_code='US' AND closing_registration_date IS NULL FROM events WHERE link_id=100"), "intake preserves geography and unknown close time");
    await conn.ExecuteAsync("DELETE FROM events WHERE link_id=100; DELETE FROM tournament_links WHERE id=100");
    await conn.ExecuteAsync("""
        INSERT INTO events(id,link_id,event_name,city,addr_state,country_code,start_time,end_time,closing_registration_date,registration_open,online_tournament)
        SELECT n,n,'Event '||n,CASE n WHEN 2 THEN 'New Orleans' ELSE 'Atlanta' END,
            CASE n WHEN 2 THEN 'LA' WHEN 3 THEN 'WV' ELSE 'GA' END,CASE n WHEN 5 THEN 'CA' ELSE 'US' END,
            (CURRENT_TIMESTAMP AT TIME ZONE 'UTC') + INTERVAL '1 day', (CURRENT_TIMESTAMP AT TIME ZONE 'UTC') + INTERVAL '2 days',
            (CURRENT_TIMESTAMP AT TIME ZONE 'UTC') + INTERVAL '12 hours',n<>6,n=4 FROM generate_series(1,7) n;
        INSERT INTO tournament_links(id,event_link,game_id,url_slug,entrants_num,last_updated) SELECT n,n,CASE n WHEN 7 THEN 99 ELSE 1 END,'tournament/test-'||n||'/event/sf6',n*10,CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM generate_series(1,7) n;
        """);
    var query = new EventQueryService(scoped,null!,null!,null!,null!);
    var preview = await query.GetUpcomingFeedEvents(filter,DateTime.UtcNow);
    Check(preview.Events.Select(e => e.TournamentId).SequenceEqual([1,2]), "game, country, states, registration and offline filters");
    Check((await query.GetUpcomingFeedEvents(filter with { Attendance="Both" },DateTime.UtcNow)).Events.Count == 3, "online opt-in bypasses geographic filter");
    Check((await query.GetUpcomingFeedEvents(filter with { Attendance="Online", States=[] },DateTime.UtcNow)).Events.Single().IsOnline, "online-only");
    Check((await query.GetUpcomingFeedEvents(filter with { City="Atlanta" },DateTime.UtcNow)).Events.Count == 1, "city filter");
    Check((await query.GetUpcomingFeedEvents(filter with { MinimumEntrants=15 },DateTime.UtcNow)).Events.Single().TournamentId == 2, "entrants filter");
    Check((await query.GetUpcomingFeedEvents(filter with { MaxEvents=1 },DateTime.UtcNow)).HasMore, "truncated preview flagged");
    var protection = new EphemeralDataProtectionProvider();
    var store = new EventFeedStore(scoped,protection);
    var request = new SaveEventFeedRequest { Name="SF Southeast",Filter=filter,Schedule=new() { IntervalHours=6 },Enabled=true };
    var options = await store.Options(default);
    Check(options.Games.Any(g => g.Name == "Street Fighter 6"), "games read from catalogue");
    Check(options.Regions.Single(r => r.Id == "southeast").States.Contains("LA") && !options.Regions.Single(r => r.Id == "southeast").States.Contains("WV"), "requested Southeast mapping");
    await conn.ExecuteAsync("INSERT INTO games VALUES(222,'New game'); INSERT INTO event_feed_regions VALUES('custom','Custom Region',ARRAY['GA'],true)");
    Check((await store.Options(default)).Games.Any(g => g.Id == 222) && (await store.Options(default)).Regions.Any(r => r.Id == "custom"), "catalogue changes require no code changes");
    await Throws<ArgumentException>(() => query.GetUpcomingFeedEvents(filter with { GameIds=[99999] },DateTime.UtcNow));
    await Throws<ArgumentException>(() => query.GetUpcomingFeedEvents(filter with { RegionIds=["unknown"] },DateTime.UtcNow));
    var feed = (await store.Save(1,null,request,default))!;
    Check(feed.ConnectionStatus == "AwaitingDiscord", "filters saved before Discord connection");
    await conn.ExecuteAsync("UPDATE event_feeds SET next_run_at=CURRENT_TIMESTAMP - INTERVAL '1 second'");
    Check(!await store.PrepareNext(query,default), "disconnected drafts never deliver");
    Check(await store.ConnectDiscord(1,feed.Id,feed.Version,webhook,"guild","channel",default), "connect selected feed");
    feed = (await store.Get(1,feed.Id,default))!;
    Check(await store.Get(2,feed.Id,default) == null, "other user cannot read feed");
    Check(await store.Save(2,feed.Id,request,default) == null, "other user cannot update feed");
    Check(!await store.Delete(2,feed.Id,default), "other user cannot delete feed");
    Check((await store.List(2,default)).Count == 0, "owner list isolation");
    var protectedValue = await conn.QuerySingleAsync<string>("SELECT destination_protected FROM event_feeds WHERE id=@id",new { id=feed.Id });
    Check(!protectedValue.Contains("abcdefghijklmnopqrstuvwxyz") && !JsonSerializer.Serialize(feed).Contains(webhook), "webhook encrypted and omitted from API model");
    await Throws<FeedConflictException>(() => store.Save(1,feed.Id,request with { ExpectedVersion=99 },default));
    await conn.ExecuteAsync("UPDATE event_feeds SET next_run_at=CURRENT_TIMESTAMP - INTERVAL '1 second'");
    var prepared = await Task.WhenAll(store.PrepareNext(query,default),store.PrepareNext(query,default));
    Check(prepared.Count(x=>x)==1 && await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_deliveries") == 1, "concurrent preparation is deduplicated");
    var outlet = new FakeOutlet();
    var sent = await Task.WhenAll(store.DispatchNext(outlet,default),store.DispatchNext(outlet,default));
    Check(sent.Count(x=>x)==1 && outlet.Count==1, "concurrent dispatch sends once");
    Check((await store.History(1,feed.Id,default)).Single().Status == "Sent", "successful delivery history");
    Check(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_deliveries WHERE destination_protected IS NOT NULL OR payload IS NOT NULL") == 0, "completed jobs scrub secrets and snapshots");
    await conn.ExecuteAsync("UPDATE event_feeds SET next_run_at=CURRENT_TIMESTAMP - INTERVAL '1 second'");
    await store.PrepareNext(query,default);
    outlet.Result = new("Retry",RetryAfterSeconds:120);
    await store.DispatchNext(outlet,default);
    Check(!await store.DispatchNext(outlet,default), "429 retry not dispatched early");
    await conn.ExecuteAsync("UPDATE event_feed_deliveries SET next_attempt_at=CURRENT_TIMESTAMP WHERE status='Pending'");
    outlet.Result = new("Unknown");
    await store.DispatchNext(outlet,default);
    Check(!(await store.Get(1,feed.Id,default))!.Enabled, "ambiguous delivery pauses feed");
    var current = (await store.Get(1,feed.Id,default))!;
    await store.Save(1,feed.Id,request with { ExpectedVersion=current.Version },default);
    await conn.ExecuteAsync("UPDATE event_feeds SET next_run_at=CURRENT_TIMESTAMP - INTERVAL '1 second'");
    await store.PrepareNext(query,default);
    await conn.ExecuteAsync("UPDATE event_feed_deliveries SET status='Sending',sending_at=CURRENT_TIMESTAMP - INTERVAL '10 minutes' WHERE status='Pending'");
    Check(await store.RecoverInterrupted(default)==1 && !(await store.Get(1,feed.Id,default))!.Enabled, "crash recovery pauses instead of duplicating");
    current = (await store.Get(1,feed.Id,default))!;
    await store.Save(1,feed.Id,request with { ExpectedVersion=current.Version,Filter=filter with { GameIds=[98765] } },default);
    await conn.ExecuteAsync("UPDATE event_feeds SET next_run_at=CURRENT_TIMESTAMP - INTERVAL '1 second'");
    await store.PrepareNext(query,default);
    Check((await store.History(1,feed.Id,default)).First().Status=="Skipped", "empty newsletter does not send");
    await ScheduleAndConnectionChecks.Run(conn, store, query, filter, request, webhook, protection, scoped);
    await HttpChecks.Run(scoped,protection,query,feed.Id);
    Check(await store.Delete(1,feed.Id,default), "owner deletes feed");
    Check(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_deliveries")==0,"deletion cascades history");
    Console.WriteLine("PASS PostgreSQL repeatable migration, actual event filters, owner isolation, encryption, version checks, concurrent delivery, retries, crash recovery and empty feeds");
}
finally { await admin.ExecuteAsync($"DROP SCHEMA {schema} CASCADE"); }

static void Check(bool value,string message) { if(!value) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T:Exception { try { await action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
sealed class FakeHttp : HttpMessageHandler
{
    public HttpStatusCode Status=HttpStatusCode.OK;
    public string Body="{\"id\":\"123\"}";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(new HttpResponseMessage(Status) { Content=new StringContent(Body,Encoding.UTF8,"application/json") });
}
sealed class FakeOutlet : IEventFeedOutlet
{
    public string Provider=>"Discord";
    public int Count;
    public DeliveryResult Result=new("Sent","test-message");
    public Task<DeliveryResult> Send(string destination,EventDigest digest,CancellationToken ct) { Interlocked.Increment(ref Count); return Task.FromResult(Result); }
}




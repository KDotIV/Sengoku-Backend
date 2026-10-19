using Dapper;
using Npgsql;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Feeds;
using SengokuProvider.Library.Services.Events;

static class ScheduleAndConnectionChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static async Task Run(NpgsqlConnection conn, EventFeedStore store, IEventFeedQueryService query, EventFeedFilter filter,
        SaveEventFeedRequest request, string webhook, IDataProtectionProvider protection, string connection)
    {
        var now = new DateTime(2026,10,13,18,0,0,DateTimeKind.Utc);
        var next = await conn.ExecuteScalarAsync<DateTime>("SELECT event_feed_next_time('Daily',NULL,time '09:30',@now)", new { now });
        Check(next == new DateTime(2026,10,14,9,30,0,DateTimeKind.Utc), "daily schedule is UTC and advances to next day");
        var daily = (await store.Save(1,null,request with { Schedule=new() { Mode="Daily", IntervalHours=null, UtcTime="09:30" } }, default))!;
        await store.ConnectDiscord(1,daily.Id,daily.Version,webhook,"guild","channel",default);
        daily = (await store.Get(1,daily.Id,default))!;
        Check(daily.Schedule.UtcTime=="09:30" && daily.NextRunAtUtc!.Value.Hour==9 && daily.NextRunAtUtc.Value.Minute==30, "daily schedule round trip");
        await store.Delete(1,daily.Id,default);
        await Throws<ArgumentException>(() => store.Save(1,null,request with { Schedule=new() { Mode="Daily",IntervalHours=null,UtcTime="25:00" } },default));
        var interval = (await store.Save(1,null,request with { Schedule=new() { IntervalHours=7 } },default))!;
        Check(interval.Schedule.IntervalHours==7, "interval is not a fixed dropdown enum");
        await store.Delete(1,interval.Id,default);

        var inserted = (await store.Save(1,null,request with { Schedule=new() { Mode="OnInsert",IntervalHours=null } },default))!;
        await store.ConnectDiscord(1,inserted.Id,inserted.Version,webhook,"guild","channel",default);
        Check(!await store.PrepareNext(query,default), "insert subscription does not replay existing catalogue");
        // Links can arrive first; incomplete metadata must not lose the insert event.
        await conn.ExecuteAsync("INSERT INTO tournament_links(id,event_link,game_id,url_slug,entrants_num) VALUES(201,201,1,'tournament/new/event/sf6',12)");
        Check(!await store.PrepareNext(query,default), "wait for tournament metadata");
        await conn.ExecuteAsync("""
          INSERT INTO events(id,link_id,event_name,city,addr_state,country_code,start_time,registration_open,online_tournament)
          VALUES(201,201,'New tournament','Atlanta','GA','US',(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')+INTERVAL '1 day',true,false);
          """);
        Check(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_notifications WHERE feed_id=@id",new {id=inserted.Id})==1,"durable insert notification");
        Check(await store.PrepareNext(query,default), "insert feed prepared");
        var delivery = new RecordingOutlet();
        await store.DispatchNext(delivery,default);
        Check(delivery.Digest?.Events.Single().Name=="New tournament", "only new tournament sent");
        await conn.ExecuteAsync("UPDATE tournament_links SET entrants_num=13,game_id=1 WHERE id=201; UPDATE events SET addr_state='GA' WHERE id=201");
        Check(!await store.PrepareNext(query,default), "updates do not announce again");
        await using (var other = new NpgsqlConnection(connection))
        {
            await other.OpenAsync();
            await using var eventTx = await conn.BeginTransactionAsync();
            await using var linkTx = await other.BeginTransactionAsync();
            await conn.ExecuteAsync("INSERT INTO events(id,link_id,event_name,city,addr_state,country_code,start_time,registration_open,online_tournament) VALUES(202,202,'Concurrent','Atlanta','GA','US',(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')+INTERVAL '1 day',true,false)",transaction:eventTx);
            await other.ExecuteAsync("INSERT INTO tournament_links(id,event_link,game_id,url_slug) VALUES(202,202,1,'tournament/concurrent/event/sf6')",transaction:linkTx);
            await eventTx.CommitAsync();
            await linkTx.CommitAsync();
        }
        Check(await store.DiscoverNewTournaments(default)==1,"concurrent metadata transactions reconciled");
        Check(await store.PrepareNext(query,default),"reconciled insert queued");
        await store.DispatchNext(delivery,default);
        Check(delivery.Digest!.Events.Single().Name=="Concurrent","reconciled event delivered");
        inserted = (await store.Get(1,inserted.Id,default))!;
        await store.Save(1,inserted.Id,request with { ExpectedVersion=inserted.Version, Filter=filter with { MaxEvents=1 },Schedule=new() { Mode="OnInsert" } },default);
        await conn.ExecuteAsync("INSERT INTO tournament_links(id,event_link,game_id,url_slug) SELECT n,201,1,'tournament/batch-'||n||'/event/sf6' FROM generate_series(203,205) n");
        for (var i=0;i<3;i++)
        {
            Check(await store.PrepareNext(query,default),"remaining insert batch retained");
            await store.DispatchNext(delivery,default);
            Check(delivery.Digest!.Events.Count==1,"per-digest max applied without losing later inserts");
        }
        Check(!await store.PrepareNext(query,default),"insert batch drained");
        await using (var rollback = await conn.BeginTransactionAsync())
        {
            await conn.ExecuteAsync("INSERT INTO tournament_links(id,event_link,game_id,url_slug) VALUES(206,201,1,'tournament/rollback/event/sf6')",transaction:rollback);
            await rollback.RollbackAsync();
        }
        Check(!await store.PrepareNext(query,default),"rolled-back insert never announced");
        await store.Delete(1,inserted.Id,default);

        var retired = (await store.Save(1,null,request with { Filter=filter with { RegionIds=["custom"] } },default))!;
        await store.ConnectDiscord(1,retired.Id,retired.Version,webhook,"guild","channel",default);
        await conn.ExecuteAsync("UPDATE event_feed_regions SET enabled=false WHERE id='custom'; UPDATE event_feeds SET next_run_at=CURRENT_TIMESTAMP-INTERVAL '1 second' WHERE id=@id",new { id=retired.Id });
        Check(await store.PrepareNext(query,default) && !(await store.Get(1,retired.Id,default))!.Enabled,"retired catalogue selection pauses only its feed");
        await store.Delete(1,retired.Id,default);

        var oauthFeed = (await store.Save(1,null,request,default))!;
        const string session = "test-only-session";
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        await conn.ExecuteAsync("INSERT INTO user_sessions VALUES(@hash,1,CURRENT_TIMESTAMP+INTERVAL '1 hour')",new { hash=Hash(session) });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["DiscordOAuth:ClientId"]="app-id",["DiscordOAuth:ClientSecret"]="test-only",["DiscordOAuth:RedirectUri"]="https://example.test/api/event-feeds/discord/callback"
        }).Build();
        using var factory = new OAuthHttpFactory();
        var oauth = new DiscordFeedConnection(config,factory,store,store);
        var url = await oauth.Begin(1,oauthFeed.Id,session,default);
        var state = url.Split("&state=")[1];
        Check(url.StartsWith("https://discord.com/oauth2/authorize?") && url.Contains("webhook.incoming"),"channel authorization scope");
        await Throws<ArgumentException>(() => oauth.Complete(2,session,state,"test-code",null,default));
        await Throws<ArgumentException>(() => oauth.Complete(1,"other-session",state,"test-code",null,default));
        Check(factory.Handler.Calls==0,"invalid session cannot exchange code");
        var connected = await oauth.Complete(1,session,state,"test-code",null,default);
        Check(connected.ConnectionStatus=="Connected" && connected.DiscordChannelId=="channel", "Discord token exchanged and attached");
        Check(!System.Text.Json.JsonSerializer.Serialize(connected).Contains("abcdefghijklmnopqrstuvwxyz"), "token never returned to frontend");
        await Throws<ArgumentException>(() => oauth.Complete(1,session,state,"test-code",null,default));
        Check(factory.Handler.Calls==1,"OAuth replay prevented");
        await store.Delete(1,oauthFeed.Id,default);
        Console.WriteLine("PASS dynamic catalogues, daily UTC/custom intervals, durable insert feeds, metadata ordering, OAuth session binding and replay protection");
    }
    sealed class RecordingOutlet : IEventFeedOutlet
    {
        public string Provider=>"Discord";
        public EventDigest? Digest;
        public Task<DeliveryResult> Send(string destination,EventDigest digest,CancellationToken ct) { Digest=digest; return Task.FromResult(new DeliveryResult("Sent","mock")); }
    }
    sealed class OAuthHttpFactory : IHttpClientFactory, IDisposable
    {
        public OAuthHandler Handler = new();
        public HttpClient CreateClient(string name) => new(Handler,false);
        public void Dispose()=>Handler.Dispose();
    }
    sealed class OAuthHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            Check(request.RequestUri!.AbsoluteUri=="https://discord.com/api/v10/oauth2/token" && request.Content!.Headers.ContentType!.MediaType=="application/x-www-form-urlencoded","OAuth token endpoint and encoding");
            Check((await request.Content!.ReadAsStringAsync(ct)).Contains("grant_type=authorization_code"),"authorization code grant");
            return new(HttpStatusCode.OK) { Content=new StringContent("""{"webhook":{"id":"123456789012345678","token":"abcdefghijklmnopqrstuvwxyz0123456789","guild_id":"guild","channel_id":"channel"}}""") };
        }
    }
}

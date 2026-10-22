using SengokuDispatcher.Library.Models.Feeds;
using Dapper;
using Npgsql;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using SengokuProvider.Library.Models.Feeds;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Users;

static class ScheduleAndConnectionChecks
{
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static async Task Reject<T>(Func<Task> action) where T:Exception { try { await action(); } catch(T) {return;} throw new Exception("Expected "+typeof(T).Name); }
    public static async Task Run(string settingsPath)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var configured=settings.RootElement.GetProperty("ConnectionStrings").GetProperty("AlexandriaConnectionString").GetString()!;
        var schema="feed_tests_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(configured); await admin.OpenAsync();
        await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        try
        {
            var connection=new NpgsqlConnectionStringBuilder(configured){SearchPath=schema}.ConnectionString;
            await using var conn=new NpgsqlConnection(connection); await conn.OpenAsync();
            await conn.ExecuteAsync("""
                CREATE TABLE users(id int PRIMARY KEY,player_id int,user_name text,email text,password text);
                CREATE TABLE user_sessions(token_hash text PRIMARY KEY,user_id int,expires_at timestamptz);
                CREATE TABLE games(id int PRIMARY KEY,game_name text);
                CREATE TABLE events(id serial PRIMARY KEY,link_id int UNIQUE,event_name text,event_description text,region text,address_id int,start_time timestamp,end_time timestamp,closing_registration_date timestamp,registration_open bool,online_tournament bool,url_slug text,last_updated timestamp,city text,addr_state text,country_code text);
                CREATE TABLE tournament_links(id int PRIMARY KEY,event_link int,game_id int,url_slug text,entrants_num int,last_updated timestamp,viewership text[]);
                """);
            await conn.ExecuteAsync((await File.ReadAllTextAsync("database/schema/event-feed-tables-before.sql")).Replace("public.",schema+"."));
            await conn.ExecuteAsync($"SET search_path={schema}");
            await conn.ExecuteAsync((await File.ReadAllTextAsync("database/schema/event-feed-functions-before.sql")).Replace("public.",schema+"."));
            var legacy=Guid.NewGuid(); var legacyDelivery=Guid.NewGuid();
            await conn.ExecuteAsync("""
                INSERT INTO users(id,player_id,user_name) VALUES(1,1,'Admin'),(2,2,'Subscriber'),(3,3,'Other');
                INSERT INTO user_permissions(id,user_type) VALUES(1,0),(2,2);
                INSERT INTO games VALUES(1,'Game');
                INSERT INTO event_feed_regions VALUES('southeast','Southeast',ARRAY['GA'],true);
                INSERT INTO event_feed_schedules VALUES('Interval','Interval',1,false,true),('Daily','Daily',NULL,true,true),('OnInsert','OnInsert',NULL,false,true);
                INSERT INTO event_feeds(id,user_id,name,filters,frequency_hours,enabled,next_run_at,destination_protected)
                VALUES(@legacy,1,'Legacy','{"GameIds":[1],"RegionIds":["southeast"],"States":[]}',1,false,CURRENT_TIMESTAMP,'encrypted-value');
                INSERT INTO event_feed_deliveries(id,feed_id,scheduled_for,status) VALUES(@legacyDelivery,@legacy,CURRENT_TIMESTAMP,'Sent');
                """,new{legacy,legacyDelivery});
            await conn.ExecuteAsync((await File.ReadAllTextAsync("database/migrations/20261020_shared_event_feeds.sql")).Replace("public.",schema+"."));
            Check(await conn.ExecuteScalarAsync<string>("SELECT destination_protected FROM event_feed_subscriptions WHERE id=@legacy",new{legacy})=="encrypted-value","migration preserves encrypted destination");
            Check(await conn.ExecuteScalarAsync<Guid>("SELECT subscription_id FROM event_feed_deliveries WHERE id=@legacyDelivery",new{legacyDelivery})==legacy,"migration preserves history");
            Check(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM information_schema.columns WHERE table_schema=@schema AND table_name='event_feeds' AND column_name IN ('destination_protected','discord_guild_id','discord_channel_id','next_run_at')",new{schema})==0,"shared definition contains no destination or dispatch state");
            var protection=new EphemeralDataProtectionProvider();
            var store=new EventFeedStore(connection,protection);
            var request=new SaveEventFeedRequest {Name="Shared",Filter=new(){GameIds=[1],RegionIds=["southeast"]},Schedule=new(){Mode="Interval",IntervalHours=1},Enabled=true};
            await Reject<UnauthorizedAccessException>(()=>store.Save(2,null,request,default));
            var feed=(await store.Save(1,null,request,default))!;
            Check((await store.List(2,default)).Any(f=>f.Id==feed.Id),"subscribers browse shared catalogue");
            var a=(await store.Subscribe(2,feed.Id,"Discord",default))!;
            var b=(await store.Subscribe(3,feed.Id,"Discord",default))!;
            Check(a.Id!=b.Id && a.FeedId==feed.Id && b.FeedId==feed.Id,"independent subscriptions");
            Check((await store.Subscribe(2,feed.Id,"Discord",default))!.Id==a.Id,"subscribe is idempotent");
            Check(await store.GetSubscription(3,a.Id,default)==null && !await store.Unsubscribe(3,a.Id,default),"subscription owner isolation");
            const string webhook="https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789";
            Check(!await store.ConnectDiscord(3,a.Id,a.Version,webhook,"guild","channel",default),"foreign connection rejected");
            await store.ConnectDiscord(2,a.Id,a.Version,webhook,"guild","channel-a",default);
            await store.ConnectDiscord(3,b.Id,b.Version,webhook,"guild","channel-b",default);
            Check((await store.Get(1,feed.Id,default))!.Version==feed.Version,"connecting subscriber does not edit definition");
            var stored=await conn.ExecuteScalarAsync<string>("SELECT destination_protected FROM event_feed_subscriptions WHERE id=@id",new{id=a.Id});
            Check(stored!=webhook && protection.CreateProtector("Sengoku.EventFeeds.Discord.v1").Unprotect(stored!)==webhook,"destination encrypted");
            await conn.ExecuteAsync("UPDATE event_feed_subscriptions SET next_run_at=CURRENT_TIMESTAMP-INTERVAL '1 second' WHERE feed_id=@id",new{id=feed.Id});
            var query=new Query(); var outlet=new Outlet();
            var preparations = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.PrepareNext(query, default)));
            Check(preparations.Count(prepared => prepared) == 2, "concurrent workers prepare each subscription once");
            Check(await store.DispatchNext(outlet,default),"first subscriber dispatched");
            outlet.Result="Unknown";
            Check(await store.DispatchNext(outlet,default),"second subscriber failure isolated");
            Check((await store.Get(1,feed.Id,default))!.Enabled,"delivery failure never disables catalogue feed");
            Check((await store.Subscriptions(2,default)).Concat(await store.Subscriptions(3,default)).Count(s=>s.Enabled)==1,"only failed subscription pauses");
            Check((await store.History(2,b.Id,default)).Count==0,"delivery history private");
            await Reject<FeedConflictException>(()=>store.Save(1,feed.Id,request with {ExpectedVersion=999},default));
            await Reject<UnauthorizedAccessException>(()=>store.Delete(2,feed.Id,default));
            // Validate admin claims from current DB permissions, including revocation without relogin.
            var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            await conn.ExecuteAsync("INSERT INTO user_sessions VALUES(@hash,1,CURRENT_TIMESTAMP+INTERVAL '1 hour')",new{hash});
            var sessions=new AccountSessionStore(connection);
            Check((await sessions.Find(token,default))!.IsAdmin,"admin permission loaded");
            await conn.ExecuteAsync("UPDATE users SET email='admin@example.test',password=@password WHERE id=1",
                new { password = AccountPassword.Hash("test-account-password") });
            Check((await sessions.Authenticate("admin@example.test","test-account-password",default))!.IsAdmin,"login returns database-backed admin identity");
            var rotated = await sessions.Create(1,token,default);
            Check(await sessions.Find(token,default)==null && (await sessions.Find(rotated,default))!.IsAdmin,"session rotation preserves current permissions");
            await sessions.Revoke(rotated,default);
            Check(await sessions.Find(rotated,default)==null,"session revocation still works after library move");
            await conn.ExecuteAsync("INSERT INTO user_sessions VALUES(@hash,1,CURRENT_TIMESTAMP+INTERVAL '1 hour')",new{hash});
            await conn.ExecuteAsync("UPDATE user_permissions SET user_type=2 WHERE id=1");
            Check(!(await sessions.Find(token,default))!.IsAdmin,"admin revocation reflected in session");
            await conn.ExecuteAsync("UPDATE user_permissions SET user_type=0 WHERE id=1");
            // OAuth state is tied to one subscription and session and consumed once.
            a=(await store.GetSubscription(2,a.Id,default))!;
            var session2=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var hash2=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session2)));
            await conn.ExecuteAsync("INSERT INTO user_sessions VALUES(@hash2,2,CURRENT_TIMESTAMP+INTERVAL '1 hour')",new{hash2});
            Check(await store.BeginAuthorization(2,a.Id,hash2,"state",default),"subscription OAuth begin");
            Check(await store.ConsumeAuthorization(3,hash2,"state",default)==null,"OAuth owner binding");
            Check((await store.ConsumeAuthorization(2,hash2,"state",default))!.SubscriptionId==a.Id,"OAuth targets subscription");
            Check(await store.ConsumeAuthorization(2,hash2,"state",default)==null,"OAuth single use");
            // OnInsert produces no idle skipped rows, and fans out only newly captured tournaments.
            Check(await store.BeginAuthorization(2,a.Id,hash2,"stale-state",default),"pending OAuth before definition edit");
            feed=(await store.Save(1,feed.Id,request with{ExpectedVersion=feed.Version,Schedule=new(){Mode="OnInsert"}},default))!;
            Check(await store.ConsumeAuthorization(2,hash2,"stale-state",default)==null,"definition edit invalidates pending authorization");
            a=(await store.GetSubscription(2,a.Id,default))!; b=(await store.GetSubscription(3,b.Id,default))!;
            await store.ConnectDiscord(2,a.Id,a.Version,webhook,"guild","a",default);
            await store.ConnectDiscord(3,b.Id,b.Version,webhook,"guild","b",default);
            Check(!await store.PrepareNext(query,default),"idle OnInsert does not enqueue empty jobs");
            await conn.ExecuteAsync("INSERT INTO events(id,link_id,event_name,start_time,online_tournament,country_code,addr_state) VALUES(101,101,'New',CURRENT_TIMESTAMP AT TIME ZONE 'UTC',false,'US','GA'); INSERT INTO tournament_links(id,event_link,game_id,url_slug) VALUES(101,101,1,'tournament/test'); SELECT event_feed_capture_tournament(101);");
            Check(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_notifications")==2,"insert fans out to each subscriber");
            Check(await store.PrepareNext(query,default) && await store.PrepareNext(query,default),"insert notifications prepare independently");
            outlet.Result="Sent"; await store.DispatchNext(outlet,default); await store.DispatchNext(outlet,default);
            Check(!await store.PrepareNext(query,default),"insert notifications consumed once");
            await HttpChecks.Run(connection,protection,query,feed.Id);
            Check(await store.Unsubscribe(2,a.Id,default) && await store.Get(3,feed.Id,default)!=null,"unsubscribe preserves shared feed");
            Check(await store.Delete(1,feed.Id,default) && await store.GetSubscription(3,b.Id,default)==null,"admin delete cascades subscriptions");
            Console.WriteLine("PASS schema migration, admin permissions, subscriptions, encryption, fan-out, failure isolation, OAuth and insert delivery");
        }
        finally { await admin.ExecuteAsync($"DROP SCHEMA {schema} CASCADE"); }
    }
    sealed class Query:IEventFeedQueryService
    {
        public Task<EventFeedPreview> GetUpcomingFeedEvents(EventFeedFilter filter,DateTime nowUtc,CancellationToken ct=default,int[]? tournamentIds=null) => Task.FromResult(new EventFeedPreview([new UpcomingFeedEvent {Name="Test",UrlSlug="tournament/test",StartTimeUtc=DateTime.UtcNow.AddDays(1)}],false,DateTime.UtcNow));
    }
    sealed class Outlet:IEventFeedOutlet
    {
        public string Provider=>"Discord";
        public string Result="Sent";
        public Task<DeliveryResult> Send(string destination,EventDigest digest,CancellationToken ct)=>Task.FromResult(new DeliveryResult(Result,"mock"));
    }
}

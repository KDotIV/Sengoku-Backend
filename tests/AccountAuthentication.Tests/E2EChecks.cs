using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.Library.Workflows.Events;
using SengokuProvider.Library.Models.Events;
using Dapper;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Workflows.Legends;
using Npgsql;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SengokuProvider.API.Authentication;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;

static class E2EChecks
{
    public static async Task CreateSchema(NpgsqlConnection db, string schema)
    {
        await db.ExecuteAsync("""
            CREATE TABLE tournament_links(id integer PRIMARY KEY, url_slug text, event_link integer);
            CREATE TABLE standings(player_id integer, tournament_link integer, entrant_id integer, placement integer, entrants_num integer, active boolean, last_updated timestamp);
            CREATE TABLE tournament_sets(id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY, playerone_id integer, playertwo_id integer,
                playerone_name text, playertwo_name text, entrantone_id integer, entranttwo_id integer, tournament_link integer, last_updated timestamp);
            CREATE TABLE bracket_paths(id integer PRIMARY KEY, tournament_link integer, tournament_name text, event_link integer, round_num text, player_id integer, last_updated timestamp, set_ids integer[]);
            CREATE TABLE bracket_matchup_keys(matchup_key text PRIMARY KEY, set_id integer UNIQUE);
            CREATE SEQUENCE bracket_matchup_set_id_seq;
            CREATE TABLE legends(id integer PRIMARY KEY,legend_name text,player_name text,player_id integer,player_link_id integer,standings integer[],last_updated timestamptz);
            """);
        var migration = (await File.ReadAllTextAsync("database/migrations/20261010_registration_bracket_e2e.sql")).Replace("public.", schema + ".");
        await db.ExecuteAsync(migration); await db.ExecuteAsync(migration);
    }
    public static async Task Run(string connectionString, NpgsqlConnection db)
    {
        var users = new UserService(connectionString, new IntakeValidator());
        var account = await users.CreateUser("OAuth account", "oauth@example.test", "OAuth test passphrase 123!");
        var placeholder = (await users.GetUserById(account))!.PlayerId;
        await db.ExecuteAsync("INSERT INTO players(id,player_name,startgg_link,user_link) VALUES(42,'Imported gamer',654321,123456)");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["StartggOAuth:ClientId"]="test-client", ["StartggOAuth:ClientSecret"]="test-secret",
            ["StartggOAuth:RedirectUri"]="https://backend.example.test/api/user/Startgg/Callback"
        }).Build();
        var provider = new Provider();
        var oauth = new StartggOAuthService(connectionString, config, provider);
        var sessions = new AccountSessionStore(connectionString);
        var token = await sessions.Create(account, null, default);
        async Task<string> State(int id, string session) => (await oauth.Begin(id, session, default)).Split("&state=")[1];
        var state = await State(account, token);
        var otherSession = await sessions.Create(account, null, default);
        await Throws<ArgumentException>(() => oauth.Complete(account, otherSession, state, "code", null, default));
        Check(provider.Calls == 0, "wrong session rejected before provider request");
        var link = await oauth.Complete(account, token, state, "code", null, default);
        Check(link.PlayerId == 42 && link.StartggUserId == 123456 && link.StartggPlayerId == 654321, "adopts imported player and preserves distinct IDs");
        Check((await sessions.Find(token, default))?.PlayerId == 42, "session reflects updated local player without stale claims");
        Check(await db.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM players WHERE id=@placeholder)", new { placeholder }), "placeholder is not destructively merged");
        Check((await oauth.GetLink(account, default))?.PlayerId == 42, "verified link read");
        await Throws<ArgumentException>(() => oauth.Complete(account, token, state, "code", null, default));
        var denied = await State(account, token);
        var calls = provider.Calls;
        await Throws<ArgumentException>(() => oauth.Complete(account, token, denied, null, "access_denied", default));
        Check(provider.Calls == calls, "denied consent never calls token endpoint");
        var expired = await State(account, token);
        await db.ExecuteAsync("UPDATE startgg_oauth_states SET expires_at=now()-interval '1 second'");
        await Throws<ArgumentException>(() => oauth.Complete(account, token, expired, "code", null, default));
        var bad = await State(account, token); provider.Errors = true;
        await Throws<HttpRequestException>(() => oauth.Complete(account, token, bad, "code", null, default));
        provider.Errors = false;
        var account2 = await users.CreateUser("Other", "other-oauth@example.test", "Different passphrase 123!");
        var token2 = await sessions.Create(account2, null, default);
        await Throws<InvalidOperationException>(async () => await oauth.Complete(account2, token2, await State(account2, token2), "code", null, default));
        Check(await oauth.GetLink(account2, default) == null, "identity conflict leaves second account unverified");
        var revoked = await State(account, token);
        await sessions.Revoke(token, default);
        await Throws<ArgumentException>(() => oauth.Complete(account, token, revoked, "code", null, default));
        Console.WriteLine("PASS OAuth session binding, replay/expiry/denial/revocation, provider errors, imported player adoption, conflict rollback");

        var legendQuery = new LegendQueryService(connectionString, null!, null!, null!);
        var legendIntake = new LegendIntakeService(connectionString, legendQuery);
        var legendOperations = new LegendsOperations(connectionString, config, legendIntake, legendQuery, null!, null!, null!, null!, null!, users);
        var legends = await legendOperations.GenerateNewLegendsByPlayerLinks([654321, 654321]);
        Check(legends.Count == 1 && legends[0].PlayerId == 42 && legends[0].PlayerLinkId == 654321 && legends[0].Standings is { Count: 0 }, "new entrant gets Legend without fabricated standings");
        await legendIntake.InsertNewLegendData(legends);
        await legendIntake.InsertNewLegendData(await legendOperations.GenerateNewLegendsByPlayerLinks([654321]));
        Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM legends WHERE player_id=42") == 1, "new entrant Legend retry is idempotent");
        Console.WriteLine("PASS first-time entrant Legend creation without history and idempotent worker replay");

        await db.ExecuteAsync("""
            INSERT INTO tournament_links(id,url_slug,event_link,start_time) VALUES
            (101,'tournament/evo-france-2026/event/street-fighter-6-ps5',201,now()+interval '1 day'),
            (102,'tournament/other/event/street-fighter-6-ps5',202,NULL);
            INSERT INTO standings VALUES(42,101,301,2,64,false,now());
            """);
        var intake = new PlayerIntakeService(connectionString, null!, null!, null!, null!);
        BracketVictoryPathData Path(int bracketId, int tournament, bool empty) => new() {
            TournamentLinkID=tournament, EventLinkID=201, TournamentName="Game event", RoundNum="Pool A", BracketId=bracketId, PlayerStartggLink=654321,
            PlayerTournamentCard=new PlayerTournamentCard { PlayerID=42, PlayerName="Imported gamer", EntrantID=301, PlayerResults=[] },
            EntrantSetCards=empty ? [] : [new EntrantSetCard { SetID=$"br:{bracketId}:set1:301:302", PlayerOneID=42, PlayerTwoID=43, EntrantOneID=301, EntrantTwoID=302, EntrantOneName="Imported gamer", EntrantTwoName="Opponent", PathStep=1, PathSetId="set1" }]
        };
        await intake.SaveVictoryPathData(Path(1,101,false));
        await intake.SaveVictoryPathData(Path(1,101,false));
        await intake.SaveVictoryPathData(Path(2,101,true));
        await intake.SaveVictoryPathData(Path(3,102,false));
        Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_paths") == 3, "idempotent distinct pools and empty paths");
        var query = (PlayerQueryService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PlayerQueryService));
        typeof(PlayerQueryService).GetField("_connectionString",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(query,connectionString);
        var paths = await query.GetBracketPathByPlayerName("imported GAMER");
        Check(paths.Count==3 && paths.Select(x=>x.BracketPathId).Distinct().Count()==3, "exact case-insensitive public player lookup preserves paths");
        Check(paths.Single(x=>x.BracketId==2).EntrantSetCards.Count==0, "empty path visible");
        Check(paths.Single(x=>x.BracketId==1).PlayerTournamentCard.PlayerResults.Single().StandingDetails.Placement==2, "actual standing");
        Check(paths.Single(x=>x.BracketId==1).EntrantSetCards.Single().PathStep==1, "candidate order persisted");
        Check((await query.GetBracketPathByTournamentSlug("https://start.gg/tournament/evo-france-2026/event/street-fighter-6-ps5")).Count==2, "full event lookup scoped to parent");
        Check((await query.GetBracketPathByTournamentSlug("street-fighter-6-ps5")).Count==3, "event suffix lookup spans matching parents without merging");
        Check((await query.GetBracketPathByPlayerName("%' OR 1=1 --")).Count==0, "player name is a literal parameter");
        Check((await query.GetBracketPathByPlayerId(42)).Count==3, "singular ID route returns all paths");
        Console.WriteLine("PASS real PostgreSQL path persistence, empty paths, standings, ordering and public searches");
        var importedEvents = 0;
        async Task<int> ImportEvent()
        {
            importedEvents++;
            return await db.ExecuteAsync("INSERT INTO tournament_links(id,url_slug,event_link) VALUES(303,'tournament/bootstrap/event/game',202)");
        }
        var events = Proxy.For<IEventOperations>((method, args) =>
        {
            Check(method == nameof(IEventOperations.IntakeTournamentIdData) && ((LinkTournamentByEventIdCommand)args![0]!).EventLinkId == 202, "bootstrap imports the verified parent tournament");
            return ImportEvent();
        });
        using var metadataClient = new GraphQLHttpClient(new GraphQLHttpClientOptions { EndPoint = new Uri("https://example.test/graphql") }, new NewtonsoftJsonSerializer(), new HttpClient(new MetadataProvider()));
        var bootstrap = new BracketTournamentBootstrap(connectionString, metadataClient, new RequestThrottler(null!), events);
        Check(await bootstrap.Ensure("tournament/bootstrap/event/game", 8, 9) == 303, "missing game event is bootstrapped");
        await bootstrap.Ensure("tournament/bootstrap/event/game", 8, 9);
        Check(importedEvents == 1, "existing tournament metadata is reused");
        Check(await db.ExecuteScalarAsync<string>("SELECT startgg_state FROM tournament_links WHERE id=303") == "ACTIVE", "actual event lifecycle persisted");
        await Throws<ArgumentException>(() => bootstrap.Ensure("tournament/wrong/event/game",8,9));
        await Throws<ArgumentException>(() => bootstrap.Ensure("tournament/bootstrap/event/game",7,9));
        Console.WriteLine("PASS event bootstrap, existing event reuse, schedule metadata and mismatched event/phase rejection");
    }
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); }
    static async Task Throws<T>(Func<Task> action) where T:Exception { try { await action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
    sealed class MetadataProvider : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"phaseGroup":{"id":9,"phase":{"id":8,"event":{"id":303,"slug":"tournament/bootstrap/event/game","startAt":1791532800,"state":"ACTIVE","tournament":{"id":202}}}}}}""", Encoding.UTF8,"application/json") });
    }
    sealed class Provider : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls; public bool Errors;
        public HttpClient CreateClient(string name) => new(this,false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var body = await request.Content!.ReadAsStringAsync(ct);
            string response;
            if(request.RequestUri!.AbsolutePath=="/oauth/access_token") {
                Check(body.Contains("test-secret") && body.Contains("authorization_code"),"server token exchange contract");
                response="{\"access_token\":\"test-access-token\"}";
            } else {
                Check(request.Headers.Authorization?.Parameter=="test-access-token" && body.Contains("currentUser"),"identity uses user OAuth token");
                response=Errors ? "{\"errors\":[{\"message\":\"denied\"}],\"data\":null}" : "{\"data\":{\"currentUser\":{\"id\":123456,\"slug\":\"user/verified\",\"player\":{\"id\":654321,\"gamerTag\":\"Imported gamer\"}}}}";
            }
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"application/json")};
        }
    }
}

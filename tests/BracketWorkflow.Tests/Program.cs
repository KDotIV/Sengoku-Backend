using System.Reflection;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Workflows.Players;
using SengokuProvider.Library.Workflows.Legends;
using SengokuProvider.Worker.Handlers;

var tests = new (string Name, Func<Task> Run)[]
{
    ("partial legends checkpoint and resume without fetching bracket again", async () =>
    {
        var f = new Fixture();
        f.Legends.Add(Fixture.Legend(20, 2));
        var pending = await f.Start();
        Check(pending.Status == "Pending" && pending.OperationId != null, "pending response");
        Check(f.Store.Checkpoint!.Data.EntrantSetCards.Count == 1 && f.Saves == 0, "partial card preserved; not saved prematurely");
        Check(f.Store.Checkpoint.MissingPlayerLinks.SequenceEqual([30]), "only missing legend requested");
        var originalCard = JsonConvert.SerializeObject(f.Store.Checkpoint.Data.EntrantSetCards[0]);
        var duplicate = await f.Start();
        Check(duplicate.OperationId == pending.OperationId && f.BracketQueries == 1, "initial request deduplicated");
        f.Legends.Clear(); // previously completed cards must survive even if lookup no longer returns that legend
        f.Legends.Add(Fixture.Legend(30, 3));
        var done = await f.Operations.ResumeBracketProcessing(pending.OperationId!.Value);
        Check(done!.Status == "Completed" && f.Saves == 1 && f.BracketQueries == 1, "resume completes once");
        Check(JsonConvert.SerializeObject(f.Store.Checkpoint.Data.EntrantSetCards[0]) == originalCard, "original card unchanged");
        Check(f.Store.Checkpoint.Data.EntrantSetCards.Count == 2, "all cards saved");
        await f.Operations.ResumeBracketProcessing(pending.OperationId.Value);
        Check(f.Saves == 1, "duplicate resume does not save again");
    }),
    ("all legends available completes synchronously", async () =>
    {
        var f = new Fixture(); f.Legends.AddRange([Fixture.Legend(20, 2), Fixture.Legend(30, 3)]);
        var result = await f.Start();
        Check(result.Status == "Completed" && f.Saves == 1 && f.Store.Messages.Count == 0, "no onboarding needed");
        Check(f.Store.Checkpoint!.Data.EventLinkID == 4 && f.Store.Checkpoint.Data.TournamentName == "Test event", "metadata preserved");
    }),
    ("missing standings bootstrap dependency intake", async () =>
    {
        var f = new Fixture { HasStandings = false };
        await f.Start();
        Check(f.Store.Messages.Any(x => x.Message is PlayerReceivedData { Command: IntakePlayersByTournamentCommand }), "bootstrap queued");
    }),
    ("existing standings avoid repeating tournament intake", async () =>
    {
        var f = new Fixture(); await f.Start();
        Check(f.Store.Messages.Count == 2 && !f.Store.Messages.Any(x => x.Message is PlayerReceivedData { Command: IntakePlayersByTournamentCommand }), "only legends and fallback resume queued");
        var onboarding = (OnboardReceivedData)f.Store.Messages[0].Message;
        Check(onboarding.Command is OnboardLegendsByPlayerLinkCommand { Topic: CommandRegistry.OnboardPlayersByLinkData }, "correct contract");
        Check(f.Store.Messages[1].AvailableAt > DateTime.UtcNow.AddSeconds(90), "fallback waits for dependencies");
    }),
    ("early completion notification does not flood onboarding", async () =>
    {
        var f = new Fixture(); var result = await f.Start();
        await f.Operations.ResumeBracketProcessing(result.OperationId!.Value);
        Check(f.Store.Messages.Count == 2 && f.Store.Checkpoint!.Attempts == 1, "retry throttled");
    }),
    ("due retry only requests remaining dependencies", async () =>
    {
        var f = new Fixture(); var result = await f.Start();
        f.Legends.Add(Fixture.Legend(20, 2));
        f.Store.Checkpoint!.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        await f.Operations.ResumeBracketProcessing(result.OperationId!.Value);
        var command = (OnboardLegendsByPlayerLinkCommand)((OnboardReceivedData)f.Store.Messages[^2].Message).Command;
        Check(command.PlayerLinkIds.SequenceEqual([30]) && f.Store.Checkpoint.Attempts == 2, "remaining only");
    }),
    ("expired operations stop intake and report status", async () =>
    {
        var f = new Fixture(); var result = await f.Start();
        f.Store.Checkpoint!.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        Check((await f.Operations.GetBracketProcessingStatus(result.OperationId!.Value))!.Status == "Expired", "status exposes expiry");
        await f.Operations.ResumeBracketProcessing(result.OperationId.Value);
        Check(f.Store.Checkpoint.Result.Status == "Expired" && f.Store.Messages.Count == 2 && f.Saves == 0, "expired operation stops");
    }),
    ("bye with no opponents completes without invalid save", async () =>
    {
        var f = new Fixture(); f.Bracket.PhaseGroup!.Sets.Nodes = [Fixture.Set("s1", 1, Fixture.Slot("a", 100, 10), new Slot { Id = "bye" })];
        var result = await f.Start();
        Check(result.Status == "Completed" && f.Saves == 0 && f.Store.Messages.Count == 0, "bye is a completed no-op");
    }),
    ("same opponent in different rounds has distinct cards", () =>
    {
        var c = Fixture.Checkpoint();
        c.ExpectedOpponents = [new(200, 20, "Opponent", "A", "Direct") { PathSetId = "1" },
            new(200, 20, "Opponent", "B", "Direct") { PathSetId = "2" }];
        Check(BracketCardBuilder.Build(c, [Fixture.Legend(20, 2)]).Length == 0 && c.Data.EntrantSetCards.Count == 2, "round-specific identities");
        return Task.CompletedTask;
    }),
    ("invalid legends remain missing", () =>
    {
        var c = Fixture.Checkpoint(); c.ExpectedOpponents = [new(200, 20, "Opponent", "A", "Direct") { PathSetId = "1" }];
        Check(BracketCardBuilder.Build(c, [Fixture.Legend(20, 0)]).SequenceEqual([20]), "invalid player cannot become a card");
        return Task.CompletedTask;
    }),
    ("resume and legends command envelopes round trip", () =>
    {
        var settings = new JsonSerializerSettings { Converters = [new CommandSerializer()] };
        var id = Guid.NewGuid();
        var resume = new PlayerReceivedData { Command = new ResumeBracketProcessingCommand { OperationId = id }, MessagePriority = MessagePriority.SystemIntake };
        var parsed = JsonConvert.DeserializeObject<PlayerReceivedData>(JsonConvert.SerializeObject(resume), settings)!;
        Check(parsed.Command is ResumeBracketProcessingCommand { OperationId: var received } && received == id, "resume converter");
        var legends = new OnboardReceivedData { Command = new OnboardLegendsByPlayerLinkCommand { Topic = CommandRegistry.OnboardPlayersByLinkData, PlayerLinkIds = [20,30], OperationId = id }, MessagePriority = MessagePriority.SystemIntake };
        var receivedLegends = JsonConvert.DeserializeObject<OnboardReceivedData>(JsonConvert.SerializeObject(legends), settings)!;
        Check(receivedLegends.Command is OnboardLegendsByPlayerLinkCommand { OperationId: var correlation } && correlation == id, "legends converter");
        return Task.CompletedTask;
    }),
    ("legend generation deduplicates standings and retains start.gg linkage", async () =>
    {
        var builds = 0;
        var intake = Stub.For<ILegendIntakeService>((name, _) =>
        {
            if (name != "BuildLegendData") throw new Exception(name);
            builds++; return Task.FromResult(new LegendData { PlayerId = 2 });
        });
        var query = Stub.For<ILegendQueryService>((name, _) => name == "QueryStandingsByPlayerId"
            ? Task.FromResult(new StandingsQueryResult { PlayerID = 2, StandingData = [] }) : throw new Exception(name));
        var players = Stub.For<IPlayerQueryService>((name, _) => name == "GetPlayerDataById"
            ? Task.FromResult(new PlayerData { Id = 2, PlayerLinkID = 20, PlayerName = "Opponent", UserLink = 1, LastUpdate = DateTime.UtcNow }) : throw new Exception(name));
        var operations = new LegendsOperations("", null!, intake, query, null!, null!, players,
            Stub.For<IAzureBusApiService>((_, _) => throw new Exception("Unexpected dependency intake")), null!, null!);
        var standing = new PlayerStandingResult { LastUpdated = DateTime.UtcNow, TournamentLinks = new Links { PlayerId = 2, EntrantId = 200 } };
        var legends = await operations.GenerateNewLegendsByPlayerStandings([standing, standing]);
        Check(builds == 1 && legends.Count == 1 && legends[0].PlayerLinkId == 20, "one linked legend per player");
    }),
    ("unsupported persisted schema stops retries", async () =>
    {
        var f = new Fixture(); var result = await f.Start(); f.Store.Checkpoint!.SchemaVersion = 99;
        var failed = await f.Operations.ResumeBracketProcessing(result.OperationId!.Value);
        Check(failed!.Status == "Failed" && f.Store.Messages.Count == 2, "unsupported schema terminal");
    }),
    ("incomplete opponent feeder sets are rejected", async () =>
    {
        var f = new Fixture(); f.Bracket.PhaseGroup!.Sets.Nodes![1].Slots![1] = new Slot { Id = "d", PrereqType = "set", PrereqId = "not-loaded" };
        try { await f.Start(); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        Check(f.Store.Checkpoint == null, "incomplete graph not mistaken for bye");
    }),
    ("team entrants are rejected explicitly", async () =>
    {
        var f = new Fixture(); f.Bracket.PhaseGroup!.Sets.Nodes![0].Slots![1].Entrant!.Participants!.Add(new Participant { Player = new() { Id = 40 } });
        try { await f.Start(); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        Check(f.Store.Checkpoint == null, "team not reduced to one player");
    }),
    ("unknown checkpoint is safe for late messages", async () =>
    {
        var f = new Fixture(); Check(await f.Operations.ResumeBracketProcessing(Guid.NewGuid()) == null, "late duplicate ignored");
    }),
    ("outbox error does not retain half-created checkpoint", async () =>
    {
        var f = new Fixture(); f.Store.FailEnqueue = true;
        try { await f.Start(); throw new Exception("Expected failure"); }
        catch (InvalidOperationException) { }
        Check(f.Store.Checkpoint == null && f.Store.Messages.Count == 0, "transaction boundary");
    }),
    ("player outside bracket is rejected", async () =>
    {
        var f = new Fixture(); f.Player.PlayerLinkID = 999;
        try { await f.Start(); throw new Exception("Expected rejection"); }
        catch (ArgumentException) { }
        Check(f.Store.Checkpoint == null && f.Store.Messages.Count == 0, "no pending operation for absent player");
    })
};
foreach (var test in tests) { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
Console.WriteLine($"{tests.Length} workflow regression tests passed.");
await PostgresChecks.Run();

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

public class Stub : DispatchProxy
{
    public Func<string, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args);
    public static T For<T>(Func<string, object?[]?, object?> handler) where T : class
    { var proxy = Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy; }
}

sealed class Fixture
{
    public MemoryStore Store { get; } = new();
    public List<LegendData> Legends { get; } = [];
    public int BracketQueries, Saves;
    public bool HasStandings = true;
    public PlayerData Player = new() { Id = 1, PlayerLinkID = 10, PlayerName = "Player", UserLink = 1, LastUpdate = DateTime.UtcNow };
    public PhaseGroupGraphQL Bracket = new() { PhaseGroup = new() { Id = 7, DisplayIdentifier = "Pool A", Sets = new() { Nodes =
        [Set("s1", 1, Slot("a", 100, 10), Slot("b", 200, 20)),
         Set("s2", 2, new Slot { Id = "c", PrereqType = "set", PrereqId = "s1", PrereqPlacement = 1 }, Slot("d", 300, 30))] } } };
    public PlayerOperations Operations { get; }
    public Fixture()
    {
        var config = Stub.For<IConfiguration>((name, args) => name == "get_Item" ? args![0]!.ToString() switch
        { "ServiceBusSettings:LegendReceivedQueue" => "legends", "ServiceBusSettings:PlayerReceivedQueue" => "players", _ => null } : throw new Exception(name));
        var players = Stub.For<IPlayerQueryService>((name, args) => name switch
        {
            "GetPlayerDataById" => Task.FromResult(Player),
            "QueryBracketDataFromStartggByBracketId" => BracketQuery(),
            "GetStandingsDataByPlayerIds" => Task.FromResult(new List<PlayerStandingResult>()),
            "GetStandingsDataByPlayerLinks" => Task.FromResult(HasStandings ? ((int[])args![0]!).Select(link => new PlayerStandingResult
                { LastUpdated = DateTime.UtcNow, TournamentLinks = new Links { PlayerId = link, PlayerLinkId = link, EntrantId = link } }).ToList() : []),
            _ => throw new Exception(name)
        });
        var events = Stub.For<IEventQueryService>((name, _) => name switch
        {
            "GetTournamentLinkbyUrl" => Task.FromResult(Tournament()),
            "GetTournamentLinksById" => Task.FromResult(new List<TournamentData> { Tournament() }),
            _ => throw new Exception(name)
        });
        var legends = Stub.For<ILegendQueryService>((name, _) => name == "GetLegendsByPlayerLink" ? Task.FromResult(Legends.ToList()) : throw new Exception(name));
        var intake = Stub.For<IPlayerIntakeService>((name, _) =>
        {
            if (name != "SaveVictoryPathData") throw new Exception(name);
            Saves++; return Task.FromResult(new PlayerOnboardResult { Response = "Saved", Status = "Completed" });
        });
        Operations = new("", config, Stub.For<ICommonDatabaseService>((_, _) => "Test event"), players, legends, events,
            Stub.For<IAzureBusApiService>((_, _) => throw new Exception("Direct bus send bypassed outbox")), intake, Store);
    }
    Task<PhaseGroupGraphQL> BracketQuery() { BracketQueries++; return Task.FromResult(Bracket); }
    public Task<PlayerOnboardResult> Start() => Operations.OnboardBracketRunnerByBracketSlug("https://start.gg/tournament/test/event/test/brackets/6/7", 1);
    static TournamentData Tournament() => new() { Id = 5, EventId = 4, UrlSlug = "test", LastUpdated = DateTime.UtcNow };
    public static LegendData Legend(int link, int player) => new() { Id = player + 100, PlayerLinkId = link, PlayerId = player };
    public static Slot Slot(string id, int entrant, int link) => new() { Id = id, Entrant = new() { Id = entrant, Participants = [new() { Player = new() { Id = link, GamerTag = $"Player {link}" } }] } };
    public static SetNode Set(string id, int round, params Slot[] slots) => new() { Id = id, Identifier = id, Round = round, Slots = slots.ToList() };
    public static BracketProcessingCheckpoint Checkpoint() => new() { RequestKey = "test", BracketId = 7, Data = new()
        { TournamentLinkID = 5, EventLinkID = 4, PlayerTournamentCard = new() { PlayerID = 1, PlayerName = "Player", EntrantID = 100, PlayerResults = [] }, EntrantSetCards = [] } };
}

sealed class MemoryStore : IBracketCheckpointStore
{
    public BracketProcessingCheckpoint? Checkpoint;
    public bool FailEnqueue;
    public List<(object Message, DateTime AvailableAt)> Messages = [];
    public Task<BracketProcessingCheckpoint?> GetAsync(Guid id) => Task.FromResult(Checkpoint?.OperationId == id ? Copy(Checkpoint) : null);
    public async Task<BracketProcessingCheckpoint> ExecuteAsync(string key, Func<BracketProcessingCheckpoint?, NpgsqlConnection, NpgsqlTransaction, Task<BracketProcessingCheckpoint>> action)
    {
        var count = Messages.Count;
        try
        {
            var result = await action(Checkpoint == null ? null : Copy(Checkpoint), null!, null!);
            result.Result.OperationId = result.OperationId; Checkpoint = Copy(result); return result;
        }
        catch { Messages.RemoveRange(count, Messages.Count - count); throw; }
    }
    public Task EnqueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string queue, object message, DateTime availableAt)
    { if (FailEnqueue) throw new InvalidOperationException("Simulated outbox failure"); Messages.Add((message, availableAt)); return Task.CompletedTask; }
    public Task EnqueueResumeAsync(Guid id, string queue) => Task.CompletedTask;
    static BracketProcessingCheckpoint Copy(BracketProcessingCheckpoint checkpoint) => JsonConvert.DeserializeObject<BracketProcessingCheckpoint>(JsonConvert.SerializeObject(checkpoint))!;
}

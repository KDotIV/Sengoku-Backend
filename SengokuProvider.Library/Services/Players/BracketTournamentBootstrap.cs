using Dapper;
using GraphQL;
using GraphQL.Client.Http;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Workflows.Events;
namespace SengokuProvider.Library.Services.Players;
public interface IBracketTournamentBootstrap { Task<int> Ensure(string eventSlug, int phaseId, int bracketId); }
public sealed class BracketTournamentBootstrap(string connectionString, GraphQLHttpClient client, RequestThrottler throttler, IEventOperations events) : IBracketTournamentBootstrap
{
    public async Task<int> Ensure(string eventSlug, int phaseId, int bracketId)
    {
        await throttler.WaitIfPaused();
        var response = await client.SendQueryAsync<JObject>(new GraphQLRequest {
            Query = "query BracketEvent($id: ID!) { phaseGroup(id: $id) { id phase { id event { id slug startAt state tournament { id } } } } }",
            Variables = new { id = bracketId }
        });
        if (response.Errors is { Length: > 0 }) throw new ApplicationException("Start.gg rejected the bracket metadata lookup.");
        var phase = response.Data?["phaseGroup"]?["phase"];
        var gameEvent = phase?["event"];
        if ((int?)phase?["id"] != phaseId || (string?)gameEvent?["slug"] != eventSlug || (int?)gameEvent?["id"] is not > 0)
            throw new ArgumentException("The bracket and phase do not belong to the supplied game event URL.");
        int id = (int)gameEvent!["id"]!;
        await using var conn = new NpgsqlConnection(connectionString);
        if (!await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM tournament_links WHERE id = @id)", new { id }))
        {
            var parentId = (int?)gameEvent["tournament"]?["id"] ?? 0;
            if (parentId <= 0) throw new ArgumentException("The bracket has no parent tournament.");
            await events.IntakeTournamentIdData(new LinkTournamentByEventIdCommand { EventLinkId = parentId, Topic = default });
            if (!await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM tournament_links WHERE id = @id)", new { id }))
                throw new ApplicationException("Tournament bootstrap did not create the requested event.");
        }
        var timestamp = (long?)gameEvent["startAt"];
        await conn.ExecuteAsync("UPDATE tournament_links SET start_time = @start, startgg_state = @state WHERE id = @id",
            new { id, start = timestamp.HasValue ? DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime : (DateTime?)null, state = (string?)gameEvent["state"] });
        return id;
    }
}

using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SengokuProvider.Library.Services.Players;

public sealed record PlayerLegendResponse(int Id, string LegendName, int PlayerId, int PlayerLinkId, string? PlayerName, int[]? Placements);

public sealed class PlayerLegendLookup(string connectionString, ILogger<PlayerLegendLookup> logger)
{
    public async Task<IEnumerable<PlayerLegendResponse>> GetByPlayerId(int playerId, CancellationToken ct)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            return await connection.QueryAsync<PlayerLegendResponse>(new CommandDefinition("""
                SELECT id, legend_name AS LegendName, player_id AS PlayerId, player_link_id AS PlayerLinkId,
                    player_name AS PlayerName, standings AS Placements
                FROM legends WHERE player_id = @playerId ORDER BY id
                """, new { playerId }, cancellationToken: ct));
        }
        catch (NpgsqlException ex)
        {
            logger.LogError("Legend lookup failed for player {PlayerId} (SQLSTATE {SqlState})", playerId, ex.SqlState);
            throw new ApplicationException("Unable to query player legends.", ex);
        }
    }
}

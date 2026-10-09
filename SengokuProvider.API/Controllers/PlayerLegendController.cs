using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
namespace SengokuProvider.API.Controllers;
[ApiController, AllowAnonymous, Route("api/players")]
public sealed class PlayerLegendController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("GetLegendByPlayerId")]
    public async Task<IActionResult> GetLegendByPlayerId([FromQuery] int playerId, CancellationToken ct)
    {
        if (playerId <= 0) return BadRequest(new { response = "A positive local player ID is required." });
        await using var conn = new NpgsqlConnection(configuration.GetConnectionString("AlexandriaConnectionString"));
        var result = await conn.QueryAsync<PlayerLegendResponse>(new CommandDefinition(@"
            SELECT id, legend_name AS LegendName, player_id AS PlayerId, player_link_id AS PlayerLinkId,
                player_name AS PlayerName, standings AS Placements FROM legends WHERE player_id = @playerId ORDER BY id",
            new { playerId }, cancellationToken: ct));
        return Ok(result);
    }
}
public sealed record PlayerLegendResponse(int Id, string LegendName, int PlayerId, int PlayerLinkId, string? PlayerName, int[]? Placements);

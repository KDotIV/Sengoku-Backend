using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.API.Controllers;

[ApiController, AllowAnonymous, Route("api/players")]
public sealed class PlayerLegendController(PlayerLegendLookup lookup) : ControllerBase
{
    [HttpGet("GetLegendByPlayerId")]
    public async Task<IActionResult> GetLegendByPlayerId([FromQuery] int playerId, CancellationToken ct)
    {
        if (playerId <= 0) return BadRequest(new { response = "A positive local player ID is required." });
        try
        {
            return Ok(await lookup.GetByPlayerId(playerId, ct));
        }
        catch (ApplicationException)
        {
            return Problem("Unable to query player legends.");
        }
    }
}

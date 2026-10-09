using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using SengokuProvider.API.Authentication;
using System.Security.Claims;
namespace SengokuProvider.API.Controllers;

[ApiController, Authorize, Route("api/user/Startgg")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StartggOAuthController(StartggOAuthService oauth) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("Link")]
    public async Task<IActionResult> Link(CancellationToken ct) => Ok(new { enabled = oauth.Enabled, link = await oauth.GetLink(UserId, ct) });

    [HttpPost("Authorize"), ValidateAntiForgeryToken, EnableRateLimiting("account-entry")]
    public async Task<IActionResult> Begin(CancellationToken ct)
    {
        if (!oauth.Enabled) return StatusCode(503, new { response = "Start.gg verification is not configured." });
        try { return Ok(new { authorizationUrl = await oauth.Begin(UserId, Request.Cookies[SessionAuthenticationHandler.CookieName]!, ct) }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
    [HttpGet("Callback")]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code, [FromQuery] string? error, CancellationToken ct)
    {
        if (!oauth.Enabled) return StatusCode(503, new { response = "Start.gg verification is not configured." });
        Response.Headers["Referrer-Policy"] = "no-referrer";
        try { return Ok(await oauth.Complete(UserId, Request.Cookies[SessionAuthenticationHandler.CookieName]!, state, code, error, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { response = ex.Message }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (InvalidOperationException ex) { return Conflict(new { response = ex.Message }); }
        catch (PostgresException ex) when (ex.SqlState is "23505" or "40001" or "40P01") { return Conflict(new { response = "The identity changed concurrently. Restart verification." }); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return StatusCode(502, new { response = "Start.gg verification timed out. Restart verification." }); }
        catch (HttpRequestException) { return StatusCode(502, new { response = "Start.gg verification failed. Restart verification." }); }
        catch (System.Text.Json.JsonException) { return StatusCode(502, new { response = "Start.gg returned an invalid response." }); }
        catch (Newtonsoft.Json.JsonException) { return StatusCode(502, new { response = "Start.gg returned an invalid response." }); }
    }
}

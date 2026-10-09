using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SengokuProvider.API.Authentication;
using System.Security.Claims;

namespace SengokuProvider.API.Controllers;

[ApiController]
[Route("api/user")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountSessionController(AccountSessionStore sessions, IConfiguration configuration) : ControllerBase
{
    [HttpGet("Csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery) =>
        Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("Login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("account-entry")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var account = await sessions.Authenticate(request.Email, request.Password, ct);
        if (account == null) return Unauthorized(new { response = "Invalid email or password." });

        var token = await sessions.Create(account.UserId, Request.Cookies[SessionAuthenticationHandler.CookieName], ct);
        Response.Cookies.Append(SessionAuthenticationHandler.CookieName, token, AccountAuthenticationConfiguration.SessionCookie(configuration));
        return Ok(account);
    }

    [HttpGet("Session")]
    [Authorize]
    public IActionResult Session() => Ok(new AccountIdentity(
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), int.Parse(User.FindFirstValue("player_id")!), User.Identity!.Name!));

    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await sessions.Revoke(Request.Cookies[SessionAuthenticationHandler.CookieName], ct);
        Response.Cookies.Delete(SessionAuthenticationHandler.CookieName, AccountAuthenticationConfiguration.SessionCookie(configuration));
        return NoContent();
    }
}

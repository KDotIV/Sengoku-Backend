using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace SengokuProvider.API.Authentication;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    AccountSessionStore sessions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AccountSession";
    public const string CookieName = "__Host-sengoku-session";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[CookieName];
        if (token == null) return AuthenticateResult.NoResult();

        var account = await sessions.Find(token, Context.RequestAborted);
        if (account == null) return AuthenticateResult.Fail("Session expired or revoked.");

        var identity = new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, account.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim("player_id", account.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        }, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

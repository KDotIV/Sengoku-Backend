using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace SengokuProvider.API.Authentication;

public static class AccountAuthenticationConfiguration
{
    public static void AddAccountAuthentication(this WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddScoped(_ => new AccountSessionStore(connectionString));
        builder.Services.AddHttpClient("StartggOAuth", client => client.Timeout = TimeSpan.FromSeconds(30)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddScoped(sp => new StartggOAuthService(connectionString, builder.Configuration, sp.GetRequiredService<IHttpClientFactory>()));
        builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization();
        var sameSite = CookieSameSite(builder.Configuration);

        builder.Services.AddAntiforgery(options => {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-sengoku-csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = sameSite;
            options.Cookie.Path = "/";
        });

        var protection = builder.Services.AddDataProtection().SetApplicationName("Sengoku.Accounts");
        var keyPath = builder.Configuration["Authentication:DataProtectionKeyPath"];

        if (!string.IsNullOrWhiteSpace(keyPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        builder.Services.AddRateLimiter(options => {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("account-entry", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
                    PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
        });

        var origins = builder.Configuration.GetSection("Authentication:AllowedOrigins").Get<string[]>() ?? [];

        if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Authority != uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || origin.EndsWith('/')))
            throw new InvalidOperationException("Authentication:AllowedOrigins must contain explicit HTTPS origins without trailing slashes.");

        builder.Services.AddCors(options => options.AddPolicy("AccountFrontend", policy => {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders("Location");
        }));
    }
    public static SameSiteMode CookieSameSite(IConfiguration configuration) => configuration["Authentication:CookieSameSite"] switch
    {
        null or "Lax" => SameSiteMode.Lax,
        "None" => SameSiteMode.None,
        _ => throw new InvalidOperationException("Authentication:CookieSameSite must be Lax or None.")
    };
    public static CookieOptions SessionCookie(IConfiguration configuration) => new() {
        HttpOnly = true, Secure = true, SameSite = CookieSameSite(configuration), Path = "/", IsEssential = true
    };
}

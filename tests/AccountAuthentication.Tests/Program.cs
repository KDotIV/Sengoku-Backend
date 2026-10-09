using System.Reflection;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Workflows.Players;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.Leagues;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dapper;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SengokuProvider.API.Authentication;
using SengokuProvider.API.Controllers;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Workflows.Users;

const string password = "A long passphrase! 123 + unicode 雪";
var hash = AccountPassword.Hash(password);
Check(hash != AccountPassword.Hash(password), "random password salts");
Check(AccountPassword.Verify(password, hash), "password verifies");
Check(!AccountPassword.Verify("wrong", hash) && !AccountPassword.Verify(password, password), "wrong and plaintext passwords rejected");
Check(!AccountPassword.Verify(password, "sengoku-pbkdf2-sha256-v1$bad$bad"), "malformed hash rejected");
Console.WriteLine("PASS password hashing checks");
if (args.Length != 1) { Console.WriteLine("SKIP database/HTTP tests: pass appsettings path for an isolated test schema."); return; }
var config = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
var connectionString = config.RootElement.GetProperty("ConnectionStrings").GetProperty("AlexandriaConnectionString").GetString()!;
var schema = "auth_tests_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(connectionString);
await admin.OpenAsync();
await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
try
{
    var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
    await using var db = new NpgsqlConnection(scoped);
    await db.OpenAsync();
    await db.ExecuteAsync("""
        CREATE TABLE players (id integer PRIMARY KEY, player_name text NOT NULL, startgg_link integer UNIQUE, user_link integer, last_updated timestamptz);
        CREATE TABLE users (id integer PRIMARY KEY, user_name text NOT NULL, email text UNIQUE NOT NULL, password text NOT NULL,
            player_id integer NOT NULL REFERENCES players(id), user_link integer NOT NULL);
        """);
    var migration = (await File.ReadAllTextAsync("database/migrations/20261009_user_authentication.sql")).Replace("public.", schema + ".");
    await db.ExecuteAsync(migration);
    await db.ExecuteAsync(migration);
    await E2EChecks.CreateSchema(db, schema);
    var users = new UserService(scoped, new IntakeValidator());
    var userId = await users.CreateUser("Test user", "Case@Example.test", password);
    var user = (await users.GetUserById(userId))!;
    Check(user.PlayerId > 0 && user.Password != password && AccountPassword.Verify(password, user.Password), "atomic registration provisions local player and hashes password");
    var before = await db.ExecuteScalarAsync<int>("SELECT count(*) FROM players");
    Check(await users.CreateUser("Duplicate", "CASE@example.test", password) == 0, "case-insensitive duplicate rejected");
    Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM players") == before, "duplicate leaves no orphan player");
    await db.ExecuteAsync("ALTER TABLE users ADD CONSTRAINT reject_registration CHECK (email <> 'rollback@example.test')");
    try { await users.CreateUser("Rollback", "rollback@example.test", password); throw new Exception("Expected failure"); }
    catch (PostgresException) { }
    Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM players") == before, "failed user insert rolls back new player");
    var sessions = new AccountSessionStore(scoped);
    Check((await sessions.Authenticate("CASE@example.test", password, default))?.UserId == userId, "login identity");
    Check(await sessions.Authenticate("case@example.test", "wrong", default) == null, "wrong password");
    var token = await sessions.Create(userId, null, default);
    Check((await sessions.Find(token, default))?.PlayerId == user.PlayerId, "session identity");
    Check(await db.ExecuteScalarAsync<bool>("SELECT NOT EXISTS(SELECT 1 FROM user_sessions WHERE token_hash = @token)", new { token }), "raw token never stored");
    var rotated = await sessions.Create(userId, token, default);
    Check(await sessions.Find(token, default) == null && await sessions.Find(rotated, default) != null, "login rotates session");
    await sessions.Revoke(rotated, default);
    Check(await sessions.Find(rotated, default) == null, "logout revokes server session");
    token = await sessions.Create(userId, null, default);
    await db.ExecuteAsync("UPDATE user_sessions SET expires_at = CURRENT_TIMESTAMP - INTERVAL '1 second'");
    Check(await sessions.Find(token, default) == null, "expired session rejected");
    Console.WriteLine("PASS isolated PostgreSQL registration rollback, uniqueness, hashing, session rotation/expiry/revocation, repeatable migration");

    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders(); builder.Logging.AddConsole(); builder.Logging.SetMinimumLevel(LogLevel.Error);
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration["Authentication:AllowedOrigins:0"] = "https://frontend.example.test";
    builder.AddAccountAuthentication(scoped);
    builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AccountSessionController).Assembly);
    builder.Services.AddScoped<IUserService>(_ => new UserService(scoped, new IntakeValidator()));
    builder.Services.AddScoped<IUserOperations>(_ => new UserOperations(null!, null!, null!, null!));
    builder.Services.AddScoped<CommandProcessor>();
    var operationId = Guid.NewGuid();
    var ownedCheckpoint = new BracketProcessingCheckpoint {
        RequestKey = "test", OperationId = operationId,
        Data = new BracketVictoryPathData { TournamentLinkID = 1, EventLinkID = 1, EntrantSetCards = [],
            PlayerTournamentCard = new PlayerTournamentCard { PlayerID = user.PlayerId!.Value, PlayerName = "test", PlayerResults = [] } }
    };
    var onboardCalls = 0;
    builder.Services.AddSingleton(Proxy.For<IPlayerOperations>((method, _) => {
        if (method == nameof(IPlayerOperations.OnboardBracketPathByBracketSlug)) {
            onboardCalls++;
            return Task.FromResult(new PlayerOnboardResult { Status = "Pending", OperationId = operationId, Response = "Waiting" });
        }
        return Task.FromResult<PlayerOnboardResult?>(new PlayerOnboardResult { Status = "Completed", OperationId = operationId, Response = "Done" });
    }));
    builder.Services.AddSingleton(Proxy.For<IPlayerQueryService>((_, _) => throw new Exception("Unexpected player query")));
    builder.Services.AddSingleton(Proxy.For<IBracketCheckpointStore>((_, _) => Task.FromResult<BracketProcessingCheckpoint?>(ownedCheckpoint)));
    await using var app = builder.Build();
    // Loopback test transport only; exercise the HTTPS cookie policy without a certificate.
    app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); });
    app.UseCors("AccountFrontend");
    app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.MapControllers();
    await app.StartAsync();
    using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };
    Check((await client.GetAsync("/api/user/Session")).StatusCode == HttpStatusCode.Unauthorized, "anonymous session is 401, no redirect");
    Check((await client.PostAsJsonAsync("/api/user/Login", new { email = "case@example.test", password })).StatusCode == HttpStatusCode.BadRequest, "login requires CSRF");
    var csrfResponse = await client.GetAsync("/api/user/Csrf");
    var csrf = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString();
    var csrfCookie = Cookie(csrfResponse, "__Host-sengoku-csrf");
    var registration = new HttpRequestMessage(HttpMethod.Post, "/api/user/CreateUser") {
        Content = JsonContent.Create(new { userName = "HTTP account", email = "http@example.test", password }) };
    registration.Headers.Add("Cookie", csrfCookie); registration.Headers.Add("X-CSRF-TOKEN", csrf);
    var registered = await client.SendAsync(registration);
    Check(registered.StatusCode == HttpStatusCode.OK, "HTTP registration");
    var registeredBody = await registered.Content.ReadFromJsonAsync<JsonElement>();
    Check(registeredBody.GetProperty("userId").GetInt32() > 0 && registeredBody.GetProperty("playerId").GetInt32() > 0, "registration returns both local IDs");
    Check(!registered.Headers.Contains("Set-Cookie"), "registration does not claim a login session");
    var login = new HttpRequestMessage(HttpMethod.Post, "/api/user/Login") { Content = JsonContent.Create(new { email = "case@example.test", password }) };
    login.Headers.Add("Cookie", csrfCookie); login.Headers.Add("X-CSRF-TOKEN", csrf);
    var response = await client.SendAsync(login);
    Check(response.StatusCode == HttpStatusCode.OK, "valid login");
    var sessionCookie = Cookie(response, SessionAuthenticationHandler.CookieName);
    var setCookie = response.Headers.GetValues("Set-Cookie").Single();
    Check(setCookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && setCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase), "secure HttpOnly session cookie");
    client.DefaultRequestHeaders.Add("Cookie", sessionCookie + "; " + csrfCookie);
    var session = await client.GetAsync("/api/user/Session");
    Check(session.StatusCode == HttpStatusCode.OK && !(await session.Content.ReadAsStringAsync()).Contains("password", StringComparison.OrdinalIgnoreCase), "session returns safe account data");
    var authedCsrf = await client.GetAsync("/api/user/Csrf");
    csrf = (await authedCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString();
    await db.ExecuteAsync("UPDATE users SET user_link=876630, startgg_verified_user_id=876630, startgg_verified_at=now(), startgg_profile='{\"slug\":\"user/test\"}' WHERE id=@userId", new { userId });
    await db.ExecuteAsync("UPDATE players SET startgg_link=456,user_link=876630 WHERE id=@id", new { id=user.PlayerId });
    foreach (var localPlayerId in new[] { user.PlayerId!.Value + 1, user.PlayerId.Value })
    {
        var onboard = new HttpRequestMessage(HttpMethod.Post, "/api/players/OnboardBracketPathByBracketSlug") {
            Content = JsonContent.Create(new { bracketSlug = "test bracket", playerId = localPlayerId }) };
        onboard.Headers.Add("X-CSRF-TOKEN", csrf);
        var onboardResponse = await client.SendAsync(onboard);
        Check(onboardResponse.StatusCode == (localPlayerId == user.PlayerId ? HttpStatusCode.Accepted : HttpStatusCode.Forbidden), "bracket write ownership");
    }
    Check(onboardCalls == 1, "unauthorized player never reaches onboarding workflow");
    Check((await client.GetAsync($"/api/players/BracketProcessing/{operationId}")).StatusCode == HttpStatusCode.OK, "owned operation readable");
    ownedCheckpoint.Data.PlayerTournamentCard.PlayerID++;
    Check((await client.GetAsync($"/api/players/BracketProcessing/{operationId}")).StatusCode == HttpStatusCode.NotFound, "other player's operation hidden");
    var logout = new HttpRequestMessage(HttpMethod.Post, "/api/user/Logout"); logout.Headers.Add("X-CSRF-TOKEN", csrf);
    Check((await client.SendAsync(logout)).StatusCode == HttpStatusCode.NoContent, "logout");
    Check((await client.GetAsync("/api/user/Session")).StatusCode == HttpStatusCode.Unauthorized, "replayed cookie rejected after logout");
    foreach (var origin in new[] { "https://frontend.example.test", "https://evil.example.test" })
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/user/Login");
        request.Headers.Add("Origin", origin); request.Headers.Add("Access-Control-Request-Method", "POST");
        var cors = await client.SendAsync(request);
        Check(cors.Headers.Contains("Access-Control-Allow-Origin") == origin.Contains("frontend"), "CORS allowlist");
    }
    var throttled = false;
    for (var attempt = 0; attempt < 12; attempt++)
        throttled |= (await client.PostAsJsonAsync("/api/user/Login", new { email = "case@example.test", password = "wrong" })).StatusCode == HttpStatusCode.TooManyRequests;
    Check(throttled, "account entry rate limiting");
    await app.StopAsync();
    await E2EChecks.Run(scoped, db);
    Console.WriteLine("PASS HTTP anonymous rejection, CSRF, secure cookies, authenticated session, logout replay and CORS allowlist");
}
finally { await admin.ExecuteAsync($"DROP SCHEMA {schema} CASCADE"); }
static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); }
static string Cookie(HttpResponseMessage response, string name) => response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(name + "=")).Split(';')[0];

public class Proxy : DispatchProxy
{
    public Func<string, object?[]?, object?> InvokeMethod = null!;
    public static T For<T>(Func<string, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, Proxy>();
        ((Proxy)(object)proxy).InvokeMethod = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!.Name, args);
}
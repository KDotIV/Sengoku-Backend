using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Dapper;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using LocalEnvironment;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Npgsql;
using SengokuDispatcher.API;
using SengokuDispatcher.Library.Configurations;
using SengokuDispatcher.Library.Services.Comms;
using SengokuDispatcher.Library.Services.Diagnostics;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuProvider.API;
using SengokuProvider.Library.Services.Users;

if (args.Length == 0) throw new ArgumentException("Supply a local settings JSON path, optionally followed by --serve.");
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "SengokuProvider.API", "SengokuProvider.API.sln"))) root = root.Parent;
if (root == null) throw new DirectoryNotFoundException("Run the local harness from a source checkout.");
Directory.SetCurrentDirectory(root.FullName);
var serve = args.Contains("--serve");
using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
var connection = settings.RootElement.GetProperty("ConnectionStrings").GetProperty("AlexandriaConnectionString").GetString()!;
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Host is not ("127.0.0.1" or "localhost" or "::1") || cs.Database?.StartsWith("sengoku_local", StringComparison.Ordinal) != true)
    throw new InvalidOperationException("This harness requires a loopback database named sengoku_local*. It cannot run against QA.");
var runtime = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
await using var db = new NpgsqlConnection(connection);
await db.OpenAsync();
if (!await db.ExecuteScalarAsync<bool>("SELECT to_regclass('public.user_sessions') IS NOT NULL"))
    await db.ExecuteAsync(await File.ReadAllTextAsync("database/schema/local-baseline.sql"), commandTimeout: 60);
await db.ExecuteAsync(await File.ReadAllTextAsync("database/schema/local-seed.sql"), new { password = AccountPassword.Hash("LocalOnly-12345!") });
await db.ExecuteAsync("""
    INSERT INTO games(id,game_name) VALUES(33945,'Guilty Gear Strive'),(48548,'Granblue Fantasy Versus Rising'),(43868,'Street Fighter 6')
    ON CONFLICT DO NOTHING;
    """);

using var rsa = RSA.Create(2048);
var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback);
certificateRequest.CertificateExtensions.Add(san.Build());
certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
certificateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
using var generatedCertificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
// Schannel requires an imported key handle; do not install a trusted certificate or weaken HTTPS cookies.
using var certificate = new X509Certificate2(generatedCertificate.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
var external = new ExternalServices();
var bus = new MemoryBus();
const string serviceKey = "local-only-dispatcher-key-12345678901234567890";
string? artworkHost = null;

WebApplicationBuilder Builder(bool dispatcher, int port)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [],
        WebRootPath = dispatcher ? null : Path.Combine(root.FullName, "SengokuProvider.API", "wwwroot") });
    // Do not inherit the developer's appsettings, environment credentials or user secrets.
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:AlexandriaConnectionString"] = connection,
        ["Dispatcher:ApiKey"] = serviceKey,
        ["Dispatcher:ServiceBusEnabled"] = "true", ["Dispatcher:SchedulingEnabled"] = "false", ["Dispatcher:DiscordEnabled"] = "false",
        ["EventFeeds:DeliveryEnabled"] = "true", ["EventFeeds:CommsDeliverablesEnabled"] = "true",
        ["EventFeeds:AssetsBaseUrl"] = artworkHost,
        ["Authentication:DataProtectionKeyPath"] = Path.Combine(runtime, "keys", dispatcher ? "dispatcher" : "provider"),
        ["GraphQLSettings:Endpoint"] = "https://startgg.local.test/graphql", ["GraphQLSettings:EventBearer"] = "local-only",
        ["ServiceBusSettings:AzureWebJobsServiceBus"] = "Endpoint=sb://unused/;SharedAccessKeyName=unused;SharedAccessKey=dGVzdA==",
        ["ServiceBusSettings:CommsDeliverablesConnection"] = "Endpoint=sb://unused/;SharedAccessKeyName=unused;SharedAccessKey=dGVzdA==",
        ["ServiceBusSettings:EventReceivedQueue"] = "events", ["ServiceBusSettings:PlayerReceivedQueue"] = "players", ["ServiceBusSettings:LegendReceivedQueue"] = "legends"
    });
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port, listen => { if (!dispatcher) listen.UseHttps(certificate); }));
    return builder;
}
void ExternalOverrides(IServiceCollection services)
{
    services.AddSingleton<ServiceBusClient>(bus);
    services.AddKeyedSingleton<ServiceBusClient>("comms", bus);
    services.AddHttpClient("DiscordFeedOAuth").ConfigurePrimaryHttpMessageHandler(external.Handler);
    services.AddHttpClient<IEventFeedOutlet, DiscordEventFeedOutlet>().ConfigurePrimaryHttpMessageHandler(external.Handler).RemoveAllLoggers();
    services.AddHttpClient<IDiscordWebhookHandler, DiscordWebhookHandler>().ConfigurePrimaryHttpMessageHandler(external.Handler).RemoveAllLoggers();
    services.AddScoped(_ => new GraphQLHttpClient(new GraphQLHttpClientOptions { EndPoint = new Uri("https://startgg.local.test/graphql") },
        new NewtonsoftJsonSerializer(), new HttpClient(external.Handler())));
}
static string Address(WebApplication app) => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var dispatcher = DispatcherApplication.Build(Builder(true, serve ? 7081 : 0), ExternalOverrides);
await dispatcher.StartAsync();
var dispatcherUrl = Address(dispatcher);
var providerBuilder = Builder(false, serve ? 7028 : 0);
providerBuilder.Configuration["Dispatcher:BaseUrl"] = dispatcherUrl;
await using var provider = ProviderApplication.Build(providerBuilder, ExternalOverrides);
provider.MapGet("/_local/messages", () => Results.Ok(external.Digests.ToArray()));
await provider.StartAsync();
artworkHost = Address(provider);
dispatcher.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscordFeedPresentationOptions>>().Value.AssetsBaseUrl = artworkHost;
// This route and all mock transports exist only in the test executable, never in either deployment.
Console.WriteLine($"Local Provider: {Address(provider)}; Dispatcher: {dispatcherUrl}");
Console.WriteLine("Synthetic login: admin@local.test / LocalOnly-12345!; external Discord and start.gg calls are captured locally.");

try
{
    if (serve)
    {
        Console.WriteLine("Use the Local Postman environment. Stop with Ctrl+C; the launcher shuts down its database.");
        await Task.WhenAll(provider.WaitForShutdownAsync(), dispatcher.WaitForShutdownAsync());
        return;
    }
    void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    async Task WaitFor(Func<Task<bool>> condition, string message)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(90);
        do { if (await condition()) return; await Task.Delay(1000); } while (DateTimeOffset.UtcNow < until);
        throw new Exception(message);
    }
    var cookies = new CookieContainer();
    using var client = new HttpClient(new HttpClientHandler {
        CookieContainer = cookies,
        ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented?.Thumbprint == certificate.Thumbprint
    }) { BaseAddress = new Uri(Address(provider)) };
    async Task Csrf()
    {
        using var response = await client.GetAsync("/api/user/Csrf"); response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.RootElement.GetProperty("requestToken").GetString());
    }
    Check((await client.GetAsync("/api/user/Session")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous session accepted");
    Check((await client.PostAsJsonAsync("/api/user/Login", new { email = "admin@local.test", password = "LocalOnly-12345!" })).StatusCode == HttpStatusCode.BadRequest, "Login accepted without CSRF");
    await Csrf();
    (await client.PostAsJsonAsync("/api/user/Login", new { email = "admin@local.test", password = "LocalOnly-12345!" })).EnsureSuccessStatusCode();
    Check(cookies.GetCookies(client.BaseAddress)[SessionAuthenticationHandler.CookieName]?.Secure == true, "Secure session cookie missing");
    (await client.GetAsync("/api/user/Session")).EnsureSuccessStatusCode();
    await Csrf(); // Antiforgery token changes when the anonymous client becomes authenticated.
    using var created = await client.PostAsJsonAsync("/api/event-feeds/create", new {
        name = "Local E2E Feed", enabled = true, providers = new[] { "Discord" }, schedule = new { mode = "OnInsert" },
        filter = new { gameIds = new[] { 33945, 48548 }, regionIds = new[] { "southeast" }, maxEvents = 5 }
    });
    created.EnsureSuccessStatusCode();
    using var definition = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
    var feedId = definition.RootElement.GetProperty("id").GetGuid();
    using var connected = await client.PostAsJsonAsync("/api/event-feeds/discord/webhook", new { feedId, webhookUrl = ExternalServices.Webhook });
    connected.EnsureSuccessStatusCode();
    using var subscriptionBody = JsonDocument.Parse(await connected.Content.ReadAsStringAsync());
    var subscriptionId = subscriptionBody.RootElement.GetProperty("id").GetGuid();
    Check(external.Acknowledgements == 1, "Connection acknowledgement did not use the local Discord stub");
    using var intake = await client.PostAsJsonAsync("/api/events/LinkTournamentByEventIdCommand", new { eventLinkId = 900001, topic = 0 });
    intake.EnsureSuccessStatusCode();
    await WaitFor(async () => await db.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT FROM event_feed_deliveries WHERE subscription_id=@subscriptionId AND status='Sent')", new { subscriptionId }), "No confirmed local delivery");
    Check(external.GraphQlCalls > 0 && external.Digests.Count == 1, "Real intake-to-digest workflow did not reach the mock external services exactly once");
    var message = external.Digests.Single();
    var timeline = message.ToString();
    Check(message.GetProperty("flags").GetInt32() == 32768 && timeline.Contains("Guilty Gear Strive") && timeline.Contains("Granblue Fantasy Versus Rising")
        && !timeline.Contains("Street Fighter 6"), "Timeline did not respect selected games");
    Check(message.GetProperty("components")[0].GetProperty("components")[1].GetProperty("items")[0].GetProperty("media")
        .GetProperty("url").GetString() == artworkHost + "/assets/game-headers/33945.jpg", "Local delivery did not use the actual artwork host");
    Check(timeline.Contains(external.BracketStart.ToUnixTimeSeconds().ToString()) && timeline.Contains(external.BracketStart.AddHours(2).ToUnixTimeSeconds().ToString()),
        "Bracket start times were lost between GraphQL intake and notification");
    Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM event_feed_edition_history WHERE subscription_id=@subscriptionId", new { subscriptionId }) == 1,
        "Multi-game tournament consumed more than one edition");
    foreach (var art in new[] { "33945.jpg", "1386.jpg", "43868.jpg", "49783.png", "48548.jpg", "610.jpg", "36963.jpg", "5582.jpg", "48599.jpg", "287.jpg", "1.jpg" })
        Check((await client.GetAsync("/assets/game-headers/" + art)).IsSuccessStatusCode, "Public game artwork unavailable: " + art);
    Check(bus.Published > 0 && bus.Completed > 0 && bus.DeadLetters.IsEmpty, "Change outbox/broker settlement failed");
    await WaitFor(async () => (await client.GetAsync("/health/ready")).IsSuccessStatusCode, "Readiness did not become healthy");
    using var internalClient = new HttpClient { BaseAddress = new Uri(dispatcherUrl) };
    Check((await internalClient.GetAsync("/internal/health/ready")).StatusCode == HttpStatusCode.Unauthorized, "Readiness leaked internal details anonymously");
    using (var scope = dispatcher.Services.CreateScope())
    {
        var wrongKeys = new DispatcherReadiness(dispatcher.Configuration, new EphemeralDataProtectionProvider(),
            scope.ServiceProvider.GetRequiredService<WorkerActivity>(), scope.ServiceProvider);
        Check(!(await wrongKeys.Check(default)).Checks["destinationKeys"], "Readiness missed the QA missing-key failure");
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var staleWorkers = new DispatcherReadiness(dispatcher.Configuration, protection, new WorkerActivity(), scope.ServiceProvider);
        Check((await staleWorkers.Check(default)).Status == "unready", "Readiness accepted workers that have never completed a cycle");
        try
        {
            bus.Available = false;
            Check(!(await scope.ServiceProvider.GetRequiredService<DispatcherReadiness>().Check(default)).Checks["queues"], "Readiness missed a broker failure");
        }
        finally { bus.Available = true; }
    }
    var guardConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:AlexandriaConnectionString"] = "Host=alexandia-qa.postgres.database.azure.com;Database=qa;Username=unused"
    }).Build();
    try { DispatcherEnvironmentGuard.Validate(guardConfig); throw new Exception("Local QA guard failed"); }
    catch (InvalidOperationException ex) when (ex.Message.StartsWith("Only the sengoku-dispatcher")) { }

    // Restart the actual Dispatcher host using the same persistent local ring and HTTP address.
    await dispatcher.StopAsync(); await dispatcher.DisposeAsync();
    dispatcher = DispatcherApplication.Build(Builder(true, new Uri(dispatcherUrl).Port), ExternalOverrides);
    await dispatcher.StartAsync();
    await WaitFor(async () => (await client.GetAsync("/health/ready")).IsSuccessStatusCode, "Keys or worker readiness did not survive restart");
    (await client.GetAsync("/api/user/Session")).EnsureSuccessStatusCode();
    Check(external.Digests.Count == 1, "Restart duplicated a previously delivered edition");
    await Csrf();
    (await client.DeleteAsync($"/api/event-feeds/{feedId}")).EnsureSuccessStatusCode();
    (await client.PostAsync("/api/user/Logout", null)).EnsureSuccessStatusCode();
    Check((await client.GetAsync("/api/user/Session")).StatusCode == HttpStatusCode.Unauthorized, "Logout did not revoke the session");
    Console.WriteLine("PASS local HTTPS login/CSRF/session, Provider -> Dispatcher connection, start.gg intake, PostgreSQL outbox -> broker -> worker -> Discord digest, readiness, QA guard, key persistence, restart deduplication and logout.");
}
finally
{
    await provider.StopAsync();
    await dispatcher.StopAsync(); await dispatcher.DisposeAsync();
}

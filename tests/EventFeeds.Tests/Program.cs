using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.DataProtection;
using SengokuDispatcher.Library.Services.Feeds;

using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Events;

const string webhook = "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789";
Check(DiscordEventFeedOutlet.ValidateWebhook(webhook) == webhook, "valid webhook");
foreach (var bad in new[] { "http://discord.com/api/webhooks/123/token", "https://localhost/", webhook.Replace("discord.com", "discord.com.evil.test"), webhook + "?thread_id=1", webhook.Replace("discord.com", "discord.com:444"), "https://user@discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789" })
    await Throws<ArgumentException>(() => Task.FromResult(DiscordEventFeedOutlet.ValidateWebhook(bad)));
Check(BroadcastLink.Parse("https://www.twitch.tv/capcomfighters")?.Provider == "Twitch" && BroadcastLink.Parse("https://www.youtube.com/watch?v=abcdefghijk")?.Provider == "YouTube" && BroadcastLink.Parse("javascript:alert(1)") == null, "safe guide broadcast references");

var filter = new EventFeedFilter { GameIds = [1], RegionIds = ["southeast"] };
await Throws<ArgumentException>(() => Task.FromResult((filter with { GameIds = [] }).Normalize()));
await Throws<ArgumentException>(() => Task.FromResult((filter with { RegionIds = ["invalid region"] }).Normalize()));
await Throws<ArgumentException>(() => Task.FromResult((filter with { LookAheadDays = 1000 }).Normalize()));
var digest = new EventDigest("@everyone Test", Enumerable.Range(0,10).Select(i => new DigestEvent(new string('a',300), "tournament/test/event/sf6", DateTime.UtcNow, "Atlanta, GA", 25)).ToArray(), true);
using var payload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(digest)));
Check(payload.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength() == 0, "mentions suppressed");
Check(payload.RootElement.GetProperty("embeds").EnumerateArray().Sum(e => e.GetProperty("title").GetString()!.Length + e.GetProperty("description").GetString()!.Length) < 6000, "Discord aggregate embed budget");
await Throws<ArgumentException>(() => Task.FromResult(DiscordEventFeedOutlet.Payload(digest with {
    Events = digest.Events.Select(e => e with { SignupSlug = "tournament/" + new string('x', 700) }).ToArray()
})));
await Throws<ArgumentException>(() => Task.FromResult(DiscordEventFeedOutlet.SignupUrl("https://evil.test")));
var transport = new FakeHttp();
using var http = new HttpClient(transport);
var discord = new DiscordEventFeedOutlet(http);
Check((await discord.Send(webhook, digest, default)).Status == "Sent", "successful delivery acknowledged");
transport.Status = HttpStatusCode.TooManyRequests; transport.Body = "{\"retry_after\":125.5}";
Check((await discord.Send(webhook, digest, default)).RetryAfterSeconds >= 126, "rate-limit delay honored");
transport.Body = "{\"retry_after\":172800}";
Check((await discord.Send(webhook, digest, default)).RetryAfterSeconds >= 172800, "long rate-limit delays are not shortened");
transport.Status = HttpStatusCode.InternalServerError;
Check((await discord.Send(webhook, digest, default)).Status == "Unknown", "ambiguous server error not blindly retried");
transport.Status = HttpStatusCode.NotFound;
Check((await discord.Send(webhook, digest, default)).Status == "InvalidDestination", "deleted webhook classified");
Console.WriteLine("PASS filter validation, Southeast preset, safe webhook URLs, Discord formatting, acknowledgements and rate limits");
await WebhookConnectionChecks.Run();
await DispatcherChecks.Run();
await ProviderBoundaryChecks.Run();
if (args.Length < 1) { Console.WriteLine("SKIP PostgreSQL checks: pass appsettings path to use a disposable test schema."); return; }
await ScheduleAndConnectionChecks.Run(args[0]);

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
sealed class FakeHttp : HttpMessageHandler
{
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Body = "{\"id\":\"mock\"}";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
}

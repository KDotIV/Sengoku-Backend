using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace LocalEnvironment;

public sealed class ExternalServices
{
    public const string Webhook = "https://discord.com/api/webhooks/123456789012345678/local-only-webhook-token-1234567890";
    public ConcurrentQueue<JsonElement> Digests { get; } = new();
    public int Acknowledgements;
    public int GraphQlCalls;
    public DateTimeOffset BracketStart { get; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(3).AddHours(1).ToUnixTimeSeconds());
    public HttpMessageHandler Handler() => new Transport(this);

    private sealed class Transport(ExternalServices state) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object payload;
            if (request.RequestUri?.Host == "discord.com")
            {
                if (request.Method == HttpMethod.Get)
                    payload = new { id = "123456789012345678", type = 1, guild_id = "123456789012345679", channel_id = "123456789012345680" };
                else
                {
                    if (request.Content?.Headers.ContentType?.MediaType == "application/json")
                    {
                        using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                        state.Digests.Enqueue(body.RootElement.Clone());
                    }
                    else Interlocked.Increment(ref state.Acknowledgements);
                    payload = new { id = "123456789012345681" };
                }
            }
            else if (request.RequestUri?.Host == "startgg.local.test")
            {
                Interlocked.Increment(ref state.GraphQlCalls);
                var now = DateTimeOffset.UtcNow;
                var query = await request.Content!.ReadAsStringAsync(ct);
                if (query.Contains("EventEntrants", StringComparison.Ordinal))
                    payload = new { data = new { @event = new { id = 900002, name = "Local Strive", numEntrants = 0,
                        entrants = new { nodes = Array.Empty<object>(), pageInfo = new { total = 0, totalPages = 0, page = 1, perPage = 50 } } } } };
                else payload = new { data = new { tournaments = new { pageInfo = new { totalPages = 1, page = 1 }, nodes = new[] {
                    new { id = 900001, name = "Local Anime Tournament", countryCode = "US", addrState = "GA", city = "Atlanta",
                        lat = 33.75, lng = -84.39, venueAddress = "1 Test Street, Atlanta, GA 30301", isOnline = false,
                        startAt = now.AddDays(3).ToUnixTimeSeconds(), endAt = now.AddDays(3).AddHours(6).ToUnixTimeSeconds(),
                        registrationClosesAt = now.AddDays(2).ToUnixTimeSeconds(), isRegistrationOpen = true, slug = "tournament/local-anime",
                        events = new[] {
                            new { id = 900002, slug = "tournament/local-anime/event/strive", startAt = state.BracketStart.ToUnixTimeSeconds(), numEntrants = 12, videogame = new { id = 33945 } },
                            new { id = 900003, slug = "tournament/local-anime/event/granblue", startAt = state.BracketStart.AddHours(2).ToUnixTimeSeconds(), numEntrants = 8, videogame = new { id = 48548 } },
                            new { id = 900004, slug = "tournament/local-anime/event/sf6", startAt = state.BracketStart.AddHours(3).ToUnixTimeSeconds(), numEntrants = 24, videogame = new { id = 43868 } }
                        } }
                } } } };
            }
            else throw new InvalidOperationException("Local external-service stub rejected an unexpected destination.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        }
    }
}

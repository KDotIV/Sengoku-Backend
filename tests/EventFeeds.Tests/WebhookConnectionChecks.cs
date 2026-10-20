using System.Net;
using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Feeds;

static class WebhookConnectionChecks
{
    public static async Task Run()
    {
        const string url = "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789";
        var store = new Store();
        using var http = new Transport();
        var service = new DiscordWebhookConnection(http, store, store);
        var request = new ConnectDiscordWebhookRequest { FeedName = " georgia strive feed ", WebhookUrl = url };
        var connected = await service.Connect(1, request, default);
        Check(connected?.ConnectionStatus == "Connected" && store.AttachedVersion == 2, "name connection uses snapshot version");
        Check(await service.Connect(2, request, default) == null && http.Calls == 1, "owner isolation before HTTP");
        await Reject<ArgumentException>(() => service.Connect(1, new() { FeedId = store.Feed.Id, FeedName = store.Feed.Name, WebhookUrl = url }, default));
        await Reject<ArgumentException>(() => service.Connect(1, new() { WebhookUrl = url }, default));
        await Reject<FeedConflictException>(() => service.Connect(1, new() { FeedId = store.Feed.Id, ExpectedVersion = 1, WebhookUrl = url }, default));
        store.Duplicate = true;
        await Reject<FeedConflictException>(() => service.Connect(1, request, default));
        store.Duplicate = false;
        await Reject<ArgumentException>(() => service.Connect(1, new() { FeedId = store.Feed.Id, WebhookUrl = "https://localhost/secret" }, default));
        Check(http.Calls == 1, "invalid inputs never contact Discord");
        await service.Connect(1, new() { FeedId = store.Feed.Id, WebhookUrl = url, ExpectedVersion = 3 }, default);
        Check(store.AttachedVersion == 3, "GUID connection");
        var attachments = store.Attachments;
        http.Status = HttpStatusCode.NotFound;
        await Reject<ArgumentException>(() => service.Connect(1, request, default));
        http.Status = HttpStatusCode.Redirect;
        await Reject<HttpRequestException>(() => service.Connect(1, request, default));
        http.Status = HttpStatusCode.OK;
        http.Body = "{\"type\":2}";
        await Reject<ArgumentException>(() => service.Connect(1, request, default));
        http.Body = "{\"type\":1,\"id\":\"123456789012345678\"}";
        await Reject<ArgumentException>(() => service.Connect(1, request, default));
        Check(store.Attachments == attachments, "failed verification never attaches");
        http.Body = Transport.ValidBody;
        store.Conflict = true;
        await Reject<FeedConflictException>(() => service.Connect(1, request, default));
        Console.WriteLine("PASS webhook name/GUID connection, ownership, ambiguity, version conflicts and provider validation");
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static async Task Reject<T>(Func<Task<EventFeedSubscriptionView?>> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    sealed class Transport : HttpMessageHandler, IHttpClientFactory
    {
        public const string ValidBody = """{"type":1,"id":"123456789012345678","guild_id":"223456789012345678","channel_id":"323456789012345678"}""";
        public string Body = ValidBody;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public int Calls;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "discord.com", "verification only, no messages or deletes");
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
    sealed class Store : IEventFeedStore, IDiscordFeedConnectionStore
    {
        public EventFeedSubscriptionView Feed = new(Guid.NewGuid(), "Georgia Strive Feed", "Discord", new(), new(), true, null, 2, "AwaitingDiscord", null, null);
        public bool Duplicate, Conflict;
        public int AttachedVersion, Attachments;
        private EventFeedDefinition Definition => new(Feed.Id, 1, Feed.Name, ["Discord"], Feed.Filter, Feed.Schedule, true, 1);
        public Task<EventFeedDefinition?> Get(int userId, Guid id, CancellationToken ct) => Task.FromResult<EventFeedDefinition?>(id == Feed.Id ? Definition : null);
        public Task<EventFeedSubscriptionView?> GetSubscription(int userId, Guid id, CancellationToken ct) => Task.FromResult(userId == 1 && id == Feed.Id ? Feed : null);
        public Task<EventFeedSubscriptionView?> Subscribe(int userId, Guid id, string provider, CancellationToken ct) => Task.FromResult(userId == 1 && id == Feed.Id ? Feed : null);
        public Task<IReadOnlyList<EventFeedSubscriptionView>> Subscriptions(int userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> Unsubscribe(int userId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<EventFeedDefinition>> List(int userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<EventFeedDefinition>>(Duplicate ? [Definition, Definition with { Id = Guid.NewGuid() }] : [Definition]);
        public Task<bool> ConnectDiscord(int userId, Guid id, int version, string webhook, string guild, string channel, CancellationToken ct)
        {
            if (Conflict) throw new FeedConflictException("Concurrent edit");
            Check(userId == 1 && id == Feed.Id && version == Feed.Version, "owner/version forwarded");
            AttachedVersion = version; Attachments++;
            Feed = Feed with { Version = version + 1, ConnectionStatus = "Connected", DiscordGuildId = guild, DiscordChannelId = channel };
            return Task.FromResult(true);
        }
        public Task<FeedCatalogue> Options(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeedDelivery>> History(int userId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<EventFeedDefinition?> Save(int userId, Guid? id, SaveEventFeedRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> Delete(int userId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> BeginAuthorization(int userId, Guid id, string session, string state, CancellationToken ct) => throw new NotSupportedException();
        public Task<DiscordAuthorizationState?> ConsumeAuthorization(int userId, string session, string state, CancellationToken ct) => throw new NotSupportedException();
    }
}

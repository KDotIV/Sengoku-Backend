using System.Net;
using System.Text;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Models.Common;

static class PlayerQueryRetryChecks
{
    public static async Task Run()
    {
        await Failure("null data without errors", (_, _) => Reply("{\"data\":null}"), [1, 1, 1], "null player data");
        await Failure("GraphQL errors", (_, _) => Reply("{\"errors\":[{\"message\":\"upstream failed\"}]}"), [1, 1, 1], "upstream failed");
        await Failure("later page exhaustion", (page, _) => page == 1 ? Reply(Page(1, 3)) : Reply("{\"data\":null}"), [1, 2, 2, 2]);
        await Failure("missing pagination metadata", (_, _) => Reply("{\"data\":{\"event\":{\"entrants\":{\"nodes\":[]}}}}"), [1, 1, 1], "pagination metadata");

        using var handler = new Responses((page, call) => call == 1 ? Reply("{\"data\":null}") : Reply(Page(page, 2)));
        using var client = Client(handler);
        var result = await Service(client).QueryPlayerDataFromStartgg(1723525);
        Assert(handler.Pages.SequenceEqual([1, 1, 2]), "successful retry must advance normally");
        Assert(result!.TournamentLink.Entrants.Nodes.Select(x => x.Id).SequenceEqual([1, 2]), "successful pages must not duplicate entrants");
        Console.WriteLine("PASS player query recovery and successful pagination");

        using var emptyHandler = new Responses((_, _) => Reply("{\"data\":{\"event\":{\"entrants\":{\"nodes\":[],\"pageInfo\":{\"totalPages\":0}}}}}"));
        using var emptyClient = Client(emptyHandler);
        var empty = await Service(emptyClient).QueryPlayerDataFromStartgg(1723525);
        Assert(empty!.TournamentLink.Entrants.Nodes.Count == 0 && emptyHandler.Pages.Count == 1, "empty event terminates");
        Console.WriteLine("PASS player query empty event terminates");
        await RateLimits();
        Console.WriteLine("10 player query retry regression checks passed (mock HTTP; no start.gg requests).");
    }

    private static async Task RateLimits()
    {
        string[] original;
        lock (BearerConstants.TokenQueue) original = BearerConstants.TokenQueue.ToArray();
        try
        {
            Tokens("test-token", "next-token");
            using (var handler = new Responses((page, call) => call is >= 2 and <= 4
                ? Reply("{}", HttpStatusCode.TooManyRequests) : Reply(Page(page, 3))))
            using (var client = Client(handler))
            {
                var service = Service(client);
                client.HttpClient.DefaultRequestHeaders.Add("X-Test", "preserve-me");
                var result = await service.QueryPlayerDataFromStartgg(1723525);
                Assert(handler.Pages.SequenceEqual([1, 2, 2, 2, 2, 3]), "rotation must retry the same page");
                Assert(handler.Bearers.SequenceEqual(["test-token", "test-token", "test-token", "test-token", "next-token", "next-token"]), "rotation must skip current token");
                Assert(result!.TournamentLink.Entrants.Nodes.Select(x => x.Id).SequenceEqual([1, 2, 3]), "rotation must retain completed pages without duplicates");
                Assert(client.HttpClient.DefaultRequestHeaders.Contains("X-Test"), "rotation must preserve other headers");
            }
            Console.WriteLine("PASS rate limit rotation resumes same page and retains data");

            Tokens("test-token", "next-token", "test-token", "third-token");
            using (var handler = new Responses((_, _) => Reply("{}", HttpStatusCode.TooManyRequests)))
            using (var client = Client(handler))
            {
                try { await Service(client).QueryPlayerDataFromStartgg(1723525); throw new Exception("Expected token exhaustion"); }
                catch (ApplicationException ex) { Assert(ex.Message.Contains("exhausting the token rotation"), "token exhaustion error"); }
                Assert(handler.Pages.SequenceEqual([1, 1, 1, 1, 1, 1, 1, 1, 1]), "all exhausted tokens must stop without looping");
                Assert(handler.Bearers.Distinct().Count() == 3, "all distinct bearers must be tried despite duplicates in the queue");
            }
            Console.WriteLine("PASS exhausted bearer pool terminates");

            Tokens();
            using (var handler = new Responses((_, _) => Reply("{}", HttpStatusCode.TooManyRequests)))
            using (var client = Client(handler))
            {
                try { await Service(client).QueryPlayerDataFromStartgg(1723525); throw new Exception("Expected token exhaustion"); }
                catch (ApplicationException ex) { Assert(ex.Message.Contains("exhausting the token rotation"), "empty token pool error"); }
                Assert(handler.Pages.Count == 3, "empty token pool cannot reset retries indefinitely");
            }
            Console.WriteLine("PASS empty bearer pool terminates safely");

            Tokens("test-token", "next-token");
            using (var handler = new Responses((_, _) => Reply("{}")))
            using (var client = Client(handler))
            {
                var throttler = new RequestThrottler(null!);
                var pause = throttler.PauseRequests(client);
                var waiting = throttler.WaitIfPaused();
                Assert(!waiting.IsCompleted, "waiters must block during cooldown");
                await pause;
                await waiting.WaitAsync(TimeSpan.FromSeconds(1));
            }
            Console.WriteLine("PASS throttler blocks waiters until cooldown finishes");
        }
        finally { Tokens(original); }
    }

    private static void Tokens(params string[] tokens)
    {
        lock (BearerConstants.TokenQueue)
        {
            BearerConstants.TokenQueue.Clear();
            foreach (var token in tokens) BearerConstants.TokenQueue.Enqueue(token);
        }
    }

    private static async Task Failure(string name, Func<int, int, HttpResponseMessage> response, int[] pages, string? cause = null)
    {
        using var handler = new Responses(response);
        using var client = Client(handler);
        try
        {
            await Service(client).QueryPlayerDataFromStartgg(1723525);
            throw new Exception($"{name}: returned incomplete data instead of aborting");
        }
        catch (ApplicationException ex)
        {
            Assert(ex.Message.Contains("after 3 attempts"), name + ": exhaustion message");
            Assert(cause == null || ex.InnerException?.Message.Contains(cause) == true, name + ": original error preserved");
        }
        Assert(handler.Pages.SequenceEqual(pages), name + ": must stop after three attempts on the failed page");
        Console.WriteLine($"PASS player query {name}");
    }

    private static PlayerQueryService Service(GraphQLHttpClient client)
    {
        var config = Stub.For<IConfiguration>((name, _) => name == "get_Item" ? "test-token" : throw new Exception(name));
        return new PlayerQueryService("", config, client, new RequestThrottler(config), null!);
    }
    private static GraphQLHttpClient Client(HttpMessageHandler handler) => new(
        new GraphQLHttpClientOptions { EndPoint = new Uri("https://unit-test.invalid/graphql"), EnableAutomaticPersistedQueries = _ => false },
        new NewtonsoftJsonSerializer(), new HttpClient(handler));
    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static string Page(int page, int totalPages) =>
        new JObject { ["data"] = new JObject { ["event"] = new JObject { ["id"] = 1723525,
            ["entrants"] = new JObject { ["nodes"] = new JArray(new JObject { ["id"] = page }),
                ["pageInfo"] = new JObject { ["totalPages"] = totalPages } } } } }.ToString();
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Responses(Func<int, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<int> Pages { get; } = [];
        public List<string?> Bearers { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var page = body["variables"]!["pageNum"]!.Value<int>();
            Pages.Add(page);
            Bearers.Add(request.Headers.Authorization?.Parameter);
            if (Pages.Count > 12) throw new OperationCanceledException("Test guard: query failed to terminate.");
            return respond(page, Pages.Count);
        }
    }
}

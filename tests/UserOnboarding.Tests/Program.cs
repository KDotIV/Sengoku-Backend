using System.Net;
using System.Reflection;
using System.Text;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;

using Newtonsoft.Json.Linq;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Library.Workflows.Users;

if (args.Length == 2 && args[0] == "--inspect-database")
{
    await SchemaInspection.Run(args[1]);
    return;
}

if (args.Length == 3 && args[0] == "--validate-migration")
{
    await SchemaInspection.Run(args[1], args[2]);
    return;
}

if (args.Length == 2 && args[0] == "--test-postgres")
{
    await PostgresChecks.Run(await SchemaInspection.LoadConnectionString(args[1]));
    return;
}

var saved = new List<(int User, int Player, CommonUserNode Profile)>();
var users = DispatchProxy.Create<IUserService, UserProxy>();
((UserProxy)(object)users).Save = (u, p, profile) =>
{
    saved.Add((u, p, profile));
    return new StartggProfileLink(u, p, profile.Id, profile.Player.Id, profile.Slug!);
};
var handler = new StubHttp();
using var client = new GraphQLHttpClient(new GraphQLHttpClientOptions { EndPoint = new Uri("https://example.test/graphql") },
    new NewtonsoftJsonSerializer(), new HttpClient(handler));
var operations = new UserOperations(users, client, new RequestThrottler(null!), null!);
foreach (var input in new[] { "b1a179d8", "user/b1a179d8", "start.gg/user/b1a179d8", "https://start.gg/user/b1a179d8/?tab=about", "https://www.start.gg/user/b1a179d8" })
    Check(UserOperations.NormalizeUserSlug(input) == "user/b1a179d8", "slug normalization");
foreach (var input in new[] { "https://evil.test/user/b1a179d8", "https://start.gg.evil.test/user/b1a179d8", "user/a/extra", "user/", "https://start.gg/tournament/a", "user/a\"}" })
    await Throws<ArgumentException>(() => Task.FromResult(UserOperations.NormalizeUserSlug(input)));
var result = await operations.LinkStartggProfileBySlug(100, 200, "start.gg/user/b1a179d8");
Check(result == new StartggProfileLink(100, 200, 876630, 456, "user/b1a179d8"), "distinct user/player IDs retained");
Check((string?)handler.Request!["variables"]!["slug"] == "user/b1a179d8", "slug sent as variable");
Check(((string)handler.Request["query"]!).Contains("$slug: String!"), "schema slug type");
await operations.LinkStartggProfileByUserId(100, 200, 876630);
Check((int?)handler.Request!["variables"]!["id"] == 876630, "numeric user ID sent as variable");
Check(((string)handler.Request["query"]!).Contains("$id: ID!"), "schema ID type");
Check(saved.All(x => x.Player == 200 && x.Profile.Player.Id == 456 && x.Profile.Id == 876630), "local identity is never replaced by external ID");
var before = saved.Count;
handler.Payload = """{"data":{"user":null}}""";
await Throws<KeyNotFoundException>(() => operations.LinkStartggProfileByUserId(100, 200, 876630));
handler.Payload = """{"data":{"user":{"id":876630,"slug":"user/b1a179d8","player":null}}}""";
await Throws<InvalidOperationException>(() => operations.LinkStartggProfileByUserId(100, 200, 876630));
handler.Payload = """{"errors":[{"message":"Denied"}],"data":{"user":{"id":876630,"slug":"user/b1a179d8","player":{"id":456}}}}""";
await Throws<ApplicationException>(() => operations.LinkStartggProfileByUserId(100, 200, 876630));
handler.Status = HttpStatusCode.ServiceUnavailable;
await Throws<GraphQLHttpRequestException>(() => operations.LinkStartggProfileByUserId(100, 200, 876630));
await Throws<ArgumentOutOfRangeException>(() => operations.LinkStartggProfileByUserId(0, 200, 876630));
await Throws<ArgumentOutOfRangeException>(() => operations.LinkStartggProfileByUserId(100, 200, 0));
await Throws<OperationCanceledException>(() => operations.LinkStartggProfileByUserId(100, 200, 876630, new CancellationToken(true)));
Check(saved.Count == before, "failed lookups never reach persistence");
Console.WriteLine("PASS profile URLs, schema variables, identity mapping, null/partial/error responses, HTTP failures, validation and cancellation");
await PostgresChecks.Run();

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
public class UserProxy : DispatchProxy
{
    public Func<int, int, CommonUserNode, StartggProfileLink> Save = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method!.Name == nameof(IUserService.SaveStartggProfile)
            ? Task.FromResult(Save((int)args![0]!, (int)args[1]!, (CommonUserNode)args[2]!))
            : throw new NotSupportedException(method.Name);
}
sealed class StubHttp : HttpMessageHandler
{
    public JObject? Request;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Payload = """{"data":{"user":{"id":876630,"name":"Oni_Shogi","slug":"user/b1a179d8","player":{"id":456,"gamerTag":"Oni_Shogi"}}}}""";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(Status) { Content = new StringContent(Payload, Encoding.UTF8, "application/json") };
    }
}

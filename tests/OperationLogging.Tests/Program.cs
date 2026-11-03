using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sengoku.Observability;
using SengokuDispatcher.Library.Configurations;
using SengokuProvider.Library.Configurations;
using SengokuProvider.Library.Services.Events;

var sink = new MemorySink();
var policy = new OperationDataPolicy();
var logger = new OperationLogger(sink, policy);
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }

using (var root = logger.Begin("Tournament.Intake", OperationFields.Of(("TournamentId", 42))))
{
    using (var child = OperationLog.Step("BuildTournamentData")) { await Task.Yield(); child.Complete(); }
    OperationLog.Event("Database.Committed", ("Committed", true));
    root.Complete();
}
var package = sink.Items.Single();
Check(package.IsFinal && package.Status == "Succeeded" && package.Stages.Count == 2 && package.Data["TournamentId"] == "42", "one nested package");
Check(package.StartTime <= package.EndTime && package.DurationMs >= 0 && package.Stages.All(s => s.Status == "Succeeded"), "timestamps/outcomes");
Check(OperationLog.Current == null && Activity.Current == null, "ambient cleanup");

sink.Items.Clear();
var childStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Task unfinished;
using (var root = logger.Begin("Unawaited.Child"))
{
    unfinished = Task.Run(async () =>
    {
        using var child = OperationLog.Step("Slow.Child"); childStarted.SetResult();
        await releaseChild.Task; child.Complete();
    });
    await childStarted.Task;
    root.Complete();
}
Check(sink.Items.Single().Status == "Incomplete" && sink.Items.Single().Data["ActiveStageCount"] == "1", "unfinished children cannot produce success");
releaseChild.SetResult(); await unfinished;
Check(sink.Items.Count == 1, "late child does not mutate or duplicate a sealed package");

sink.Items.Clear();
await Task.WhenAll(Enumerable.Range(0, 40).Select(async n =>
{
    using var root = logger.Begin("Concurrent.Operation", OperationFields.Of(("UserId", n)));
    await Task.Yield();
    await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
    {
        using var child = OperationLog.Step("Parallel.Child", OperationFields.Of(("UserId", n)));
        await Task.Yield(); child.Complete();
    }));
    root.Complete();
}));
Check(sink.Items.Count == 40 && sink.Items.Select(p => p.OperationId).Distinct().Count() == 40, "request isolation");
Check(sink.Items.All(p => p.Stages.Count == 8 && p.Stages.All(s => s.Data["UserId"] == p.Data["UserId"])), "parallel stage isolation");

sink.Items.Clear();
var secret = "DO-NOT-LOG-SECRET";
using (var root = logger.Begin("Privacy.Test", OperationFields.Of(("Password", secret), ("Token", secret), ("ApiKey", secret),
    ("Unknown", secret), ("UserId", new { Password = secret }), ("Username", "reviewed-user"))))
{
    OperationLog.Failure(new InvalidOperationException(secret, new ArgumentException(secret)));
    using var provider = new OperationMarkerProvider();
    provider.CreateLogger("Sengoku.Test").Log(LogLevel.Error, new EventId(17), secret, new Exception(secret), (_, _) => throw new Exception("Formatter must not run"));
    root.Complete();
}
var serialized = JsonSerializer.Serialize(sink.Items);
Check(!serialized.Contains(secret) && !serialized.Contains("Password") && serialized.Contains("System.ArgumentException"), "secrets excluded including exceptions/log text");
Check(sink.Items.Single().Status == "CompletedWithFailures", "swallowed failures not reported as success");
Check(OperationDataPolicy.SessionFingerprint(new string('A', 64)) == OperationDataPolicy.SessionFingerprint(new string('A', 64)) &&
    OperationDataPolicy.SessionFingerprint(new string('A', 64)) != OperationDataPolicy.SessionFingerprint(new string('B', 64)), "session correlation");
Check(OperationDataPolicy.SessionFingerprint("invalid") == null, "invalid session ignored");
foreach (var forbidden in new[] { "api_key", "AuthHeader", "PasswordHash", "connection_string", "Payload", "Email" })
{
    try { policy.Allow(forbidden, _ => secret); throw new Exception("Sensitive field was allowed"); }
    catch (ArgumentException) { passed++; }
}

sink.Items.Clear();
using (var root = logger.Begin("Large.Workflow"))
{
    for (var n = 0; n < 500; n++) OperationLog.Event("Stage.Completed", ("ItemCount", n), ("Username", new string('界', 100)));
    root.Complete();
}
var parts = sink.Items.OrderBy(p => p.PartIndex).ToArray();
Check(parts.Length > 1 && parts.Count(p => p.IsFinal) == 1 && parts[^1].IsFinal && parts.Select(p => p.OperationId).Distinct().Count() == 1, "chunk identity and final marker");
Check(parts.SelectMany(p => p.Stages).Select(s => s.Sequence).SequenceEqual(Enumerable.Range(1, 500).Select(i => (long)i)), "all stages preserved across chunks");
Check(parts.All(p => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(p.Stages)) < 8192 && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(p)) < 64000), "Azure size limits including Unicode escaping");
Check(parts.Select(p => p.PartIndex).SequenceEqual(Enumerable.Range(0, parts.Length)), "contiguous part indices");

sink.Items.Clear();
using (var root = logger.Begin("Cancelled")) root.Fail(new OperationCanceledException(secret));
using (logger.Begin("Interrupted")) { }
Check(sink.Items.Any(p => p.Status == "Cancelled") && sink.Items.Any(p => p.Status == "Incomplete"), "cancellation and incomplete outcome");
var failures = TelemetryHealth.FailedWrites;
using (var root = new OperationLogger(new ThrowingSink(), policy).Begin("SinkFailure")) root.Complete();
Check(TelemetryHealth.FailedWrites == failures + 1 && OperationLog.Current == null, "telemetry cannot fail business operation");

sink.Items.Clear();
var services = new ServiceCollection().AddSingleton<IOperationSink>(sink).AddOperationLogging();
services.AddScoped<ITestService, TestService>();
services.AddScoped<IDisposableService>(_ => new DisposableService());
services.InstrumentOperations(typeof(ITestService).Assembly);
services.InstrumentOperations(typeof(ITestService).Assembly);
await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
await using (var scope = provider.CreateAsyncScope())
{
    var test = scope.ServiceProvider.GetRequiredService<ITestService>();
    Check(test.Sync(7) == 7 && await test.Async(9) == 9 && await test.Value(11) == 11, "proxy preserves sync/Task/ValueTask results");
    await test.NoResult(); await test.NoValueResult();
    var probe = await test.Echo(new Sengoku.Tests.MetadataProbe { UserId = 52 });
    Check(probe.UserId == 52, "scalar DTO projection preserves result and never reads secret getters");
    var stream = new List<int>(); await foreach (var item in test.Stream()) stream.Add(item);
    Check(stream.SequenceEqual(new[] { 1, 2 }), "async stream results");
    try { await test.Fail(); throw new Exception("Expected failure"); } catch (SentinelException) { }
    try { await test.Cancel(); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
    try { test.SyncFail(); throw new Exception("Expected sync failure"); } catch (SentinelException) { passed++; }
    scope.ServiceProvider.GetRequiredService<IDisposableService>().Touch();
}
Check(DisposableService.DisposeCount == 1, "factory-created disposable owned exactly once");
Check(sink.Items.Any(p => p.Title == "TestService.Fail" && p.Status == "Failed") && sink.Items.Any(p => p.Title == "TestService.Cancel" && p.Status == "Cancelled"), "async proxy outcomes");
Check(sink.Items.Count(p => p.Title == "TestService.Async") == 1, "registration is idempotent");
Check(sink.Items.Single(p => p.Title == "TestService.Async").Data["UserId"] == "9", "field names are canonical regardless of argument casing");
Check(sink.Items.Single(p => p.Title == "TestService.Echo").Data["UserId"] == "52", "approved result metadata recorded");
Check(OperationLog.Current == null, "proxy ambient cleanup");

sink.Items.Clear();
var http = new DefaultHttpContext();
http.Request.Path = "/token/" + secret;
http.Request.QueryString = new QueryString("?code=" + secret);
http.Request.Headers.Cookie = "__Host-sengoku-session=" + new string('A', 64);
http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "12"), new Claim(ClaimTypes.Name, "tester") }, "AccountSession"));
await new OperationMiddleware(context => { context.Response.StatusCode = 403; return Task.CompletedTask; }).InvokeAsync(http, logger);
Check(sink.Items.Single().Data["SessionActive"] == "true" && sink.Items.Single().Data.ContainsKey("SessionFingerprint") && sink.Items.Single().Status == "Failed", "middleware authenticated rejection");
Check(!JsonSerializer.Serialize(sink.Items).Contains(secret), "raw paths/query never captured");
sink.Items.Clear();
try { await new OperationMiddleware(_ => throw new SentinelException()).InvokeAsync(new DefaultHttpContext(), logger); } catch (SentinelException) { }
Check(sink.Items.Single().Data["HttpStatusCode"] == "500", "unhandled exception status");

sink.Items.Clear();
using (var root = logger.Begin("Parent"))
{
    var parent = Activity.Current!.Id;
    using (ExecutionContext.SuppressFlow())
    {
        var task = Task.Run(() => { using var child = logger.Begin("Remote", traceParent: parent); child.Complete(); });
        // Wait synchronously only for this small context-propagation test, so SuppressFlow disposes on its originating thread.
        task.GetAwaiter().GetResult();
    }
    root.Complete();
}
Check(sink.Items.Select(p => p.TraceId).Distinct().Count() == 1 && sink.Items.Select(p => p.OperationId).Distinct().Count() == 2,
    "remote correlation with distinct attempts: " + JsonSerializer.Serialize(sink.Items.Select(p => new { p.Title, p.TraceId, p.OperationId })));

sink.Items.Clear();
using (var diagnostics = new OperationDiagnostics())
{
    await diagnostics.StartAsync(default);
    using var source = new ActivitySource("Npgsql");
    using (var root = logger.Begin("Database.Test"))
    {
        using (var query = source.StartActivity("SELECT password FROM users WHERE secret=" + secret))
        {
            query!.SetTag("db.statement", secret);
            query.SetStatus(ActivityStatusCode.Error, secret);
        }
        root.Complete();
    }
}
Check(sink.Items.Single().Stages.Single().Title == "Dependency.PostgreSQL" && !JsonSerializer.Serialize(sink.Items).Contains(secret), "dependency capture excludes SQL and error descriptions");

// Validate the real composition, including interfaces sharing concrete EventQueryService/EventFeedStore.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
    ["ConnectionStrings:AlexandriaConnectionString"] = "Host=localhost;Database=unused;Username=unused;Password=unused", ["GraphQLSettings:Bearer"] = "unused"
});
builder.Services.AddSingleton<IOperationSink>(sink).AddOperationLogging();
builder.AddAccountAuthentication(builder.Configuration.GetConnectionString("AlexandriaConnectionString")!);
builder.AddDispatcher(backgroundHost: false); builder.AddEventFeeds(backgroundHost: false);
builder.Services.InstrumentOperations(typeof(IEventQueryService).Assembly, typeof(DispatcherConfiguration).Assembly);
await using (var app = builder.Build())
await using (var scope = app.Services.CreateAsyncScope())
{
    var contracts = builder.Services.Where(d => !d.IsKeyedService && d.ServiceType.IsInterface &&
        (d.ServiceType.Assembly == typeof(IEventQueryService).Assembly || d.ServiceType.Assembly == typeof(DispatcherConfiguration).Assembly)).Select(d => d.ServiceType).Distinct().ToArray();
    foreach (var contract in contracts) Check(scope.ServiceProvider.GetRequiredService(contract) != null, "resolve " + contract.Name);
}
await ExporterChecks.Run();
Console.WriteLine($"PASS: {passed} operation logging checks plus Azure exporter transport.");

public sealed class MemorySink : IOperationSink { public ConcurrentBag<OperationPackage> Items { get; } = new(); public void Write(OperationPackage package) => Items.Add(package); }
public sealed class ThrowingSink : IOperationSink { public void Write(OperationPackage package) => throw new Exception("Exporter unavailable"); }
public sealed class SentinelException : Exception;
public interface ITestService
{
    int Sync(int userId); Task<int> Async(int userId); ValueTask<int> Value(int userId);
    Task NoResult(); ValueTask NoValueResult(); Task Fail(); Task Cancel(); IAsyncEnumerable<int> Stream();
    Task<Sengoku.Tests.MetadataProbe> Echo(Sengoku.Tests.MetadataProbe input); void SyncFail();
}
public sealed class TestService : ITestService
{
    public int Sync(int userId) => userId;
    public async Task<int> Async(int userId) { await Task.Yield(); return userId; }
    public async ValueTask<int> Value(int userId) { await Task.Yield(); return userId; }
    public async Task NoResult() => await Task.Yield();
    public async ValueTask NoValueResult() => await Task.Yield();
    public async Task Fail() { await Task.Yield(); throw new SentinelException(); }
    public async Task Cancel() { await Task.Yield(); throw new OperationCanceledException(); }
    public async IAsyncEnumerable<int> Stream() { yield return 1; await Task.Yield(); yield return 2; }
    public Task<Sengoku.Tests.MetadataProbe> Echo(Sengoku.Tests.MetadataProbe input) => Task.FromResult(input);
    public void SyncFail() => throw new SentinelException();
}
public interface IDisposableService { void Touch(); }
public sealed class DisposableService : IDisposableService, IDisposable
{
    public static int DisposeCount; public void Touch() { } public void Dispose() => DisposeCount++;
}

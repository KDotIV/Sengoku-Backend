using Dapper;
using Npgsql;
using SengokuDispatcher.Library.Services.Diagnostics;
using SengokuDispatcher.Library.Services.Remote;

namespace SengokuProvider.API;

public static class ProviderReadiness
{
    public static void MapProviderReadiness(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { service = "SengokuProvider" }));
        app.MapGet("/health/ready", async (IConfiguration config, DispatcherClient dispatcher, CancellationToken cancellationToken) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var checks = new Dictionary<string, bool> { ["database"] = false, ["dispatcher"] = false, ["matchingRevision"] = false };
            try
            {
                await using var db = new NpgsqlConnection(config.GetConnectionString("AlexandriaConnectionString"));
                await db.OpenAsync(timeout.Token);
                await db.ExecuteAsync(new CommandDefinition("SELECT token_hash,user_id,expires_at FROM user_sessions WHERE false", cancellationToken: timeout.Token));
                checks["database"] = true;
                using var response = await dispatcher.Send(HttpMethod.Get, "internal/health/ready", null, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    var report = await response.Content.ReadFromJsonAsync<ReadinessReport>(timeout.Token);
                    checks["dispatcher"] = report?.Status == "ready";
                    checks["matchingRevision"] = report?.Revision == DispatcherReadiness.Revision;
                }
            }
            catch (Exception ex) when (ex is NpgsqlException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException) { }
            var ready = checks.Values.All(value => value);
            // Public response deliberately omits dependency details; Dispatcher details require its internal key.
            return Results.Json(new { status = ready ? "ready" : "unready", revision = DispatcherReadiness.Revision }, statusCode: ready ? 200 : 503);
        });
    }
}

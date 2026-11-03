using System.IO.Compression;
using System.Diagnostics.Tracing;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sengoku.Observability;

internal static class ExporterChecks
{
    internal static async Task Run()
    {
        using var diagnostics = new ExportDiagnostics();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapPost("/{**path}", async (HttpRequest request) =>
        {
            Stream body = request.Body;
            if (request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase))
                body = new GZipStream(body, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(body);
            received.TrySetResult(await reader.ReadToEndAsync());
            return Results.Json(new { itemsReceived = 1, itemsAccepted = 1, errors = Array.Empty<object>() });
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=" + address,
            ["OperationLogging:ServiceName"] = "Sengoku.OperationLogging.Tests"
        }).Build();
        using (var sink = new AzureOperationSink(config))
        {
            var logger = new OperationLogger(sink, new OperationDataPolicy());
            using (var root = logger.Begin("Exporter.Contract"))
            {
                OperationLog.Event("Database.Committed", ("Committed", true));
                root.Complete();
            }
            // Exercise the real exporter/serializer and its normal batching against a local fake ingestion endpoint.
            string payload;
            try { payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (TimeoutException) { throw new InvalidOperationException("Exporter diagnostics: " + string.Join(" | ", diagnostics.Events) + "; failed writes=" + TelemetryHealth.FailedWrites); }
            if (!payload.Contains("Sengoku.Operation") || !payload.Contains("EventData") ||
                !payload.Contains("Exporter.Contract") || !payload.Contains("Database.Committed") || !payload.Contains("OperationId"))
                throw new InvalidOperationException("Azure exporter did not send the expected custom-event package.");
        }
        await app.StopAsync();
        Console.WriteLine("PASS real Azure Monitor exporter custom-event payload against local ingestion endpoint.");
    }
    private sealed class ExportDiagnostics : EventListener
    {
        internal ConcurrentQueue<string> Events { get; } = new();
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name.Contains("OpenTelemetry", StringComparison.Ordinal)) EnableEvents(source, EventLevel.Warning);
        }
        protected override void OnEventWritten(EventWrittenEventArgs args) => Events?.Enqueue(args.EventName + ": " + string.Join(";", args.Payload?.Select(value => value?.ToString()) ?? []));
    }
}

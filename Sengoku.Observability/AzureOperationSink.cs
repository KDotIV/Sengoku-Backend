using System.Text.Json;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Sengoku.Observability;

/// <summary>Private exporter pipeline: legacy ILogger text and automatic HTTP/SQL payloads never enter it.</summary>
public sealed class AzureOperationSink : IOperationSink, IDisposable
{
    private readonly ILoggerFactory factory;
    private readonly ILogger logger;
    public bool Enabled { get; }
    public AzureOperationSink(IConfiguration configuration)
    {
        var connection = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        Enabled = !string.IsNullOrWhiteSpace(connection);
        factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            if (!Enabled) return;
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeScopes = false;
                options.IncludeFormattedMessage = false;
                options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(
                    configuration["OperationLogging:ServiceName"] ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Sengoku"));
                options.AddAzureMonitorLogExporter(exporter =>
                {
                    exporter.ConnectionString = connection;
                    exporter.EnableLiveMetrics = false;
                    exporter.EnableTraceBasedLogsSampler = false;
                    exporter.DisableOfflineStorage = false;
                    exporter.StorageDirectory = configuration["OperationLogging:StorageDirectory"];
                });
            });
        });
        logger = factory.CreateLogger("Sengoku.OperationPackage");
    }
    public void Write(OperationPackage package)
    {
        if (!Enabled) return;
        var attributes = new List<KeyValuePair<string, object?>>
        {
            new("microsoft.custom_event.name", "Sengoku.Operation"),
            new("SchemaVersion", package.SchemaVersion), new("OperationId", package.OperationId),
            new("TraceId", package.TraceId), new("SpanId", package.SpanId), new("ParentSpanId", package.ParentSpanId),
            new("Title", package.Title), new("StartTime", package.StartTime.ToString("O")),
            new("EndTime", package.EndTime?.ToString("O")), new("DurationMs", package.DurationMs),
            new("Status", package.Status), new("PartIndex", package.PartIndex), new("IsFinal", package.IsFinal),
            new("StageCount", package.StageCount), new("FailedStageCount", package.FailedStageCount),
            new("Data", JsonSerializer.Serialize(package.Data)), new("Stages", JsonSerializer.Serialize(package.Stages))
        };
        logger.Log(LogLevel.Information, new EventId(1, "OperationPackage"), attributes, null, static (_, _) => "Sengoku.Operation");
    }
    public void Dispose() => factory.Dispose();
}

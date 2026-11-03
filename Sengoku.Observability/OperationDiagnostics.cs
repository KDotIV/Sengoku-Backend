using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sengoku.Observability;

/// <summary>Capture only timing/type/status of dependencies. SQL and URLs are intentionally never read.</summary>
public sealed class OperationDiagnostics(IOperationSink? sink = null, ILogger<OperationDiagnostics>? logger = null) : IHostedService, IDisposable
{
    private ActivityListener? listener;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (sink is AzureOperationSink { Enabled: false })
            logger?.LogWarning("Operation export is disabled: configure APPLICATIONINSIGHTS_CONNECTION_STRING to send operation packages to Azure.");
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql" || source.Name == "System.Net.Http" || source.Name.StartsWith("Azure.Messaging.ServiceBus", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => OperationLog.Current == null ? ActivitySamplingResult.PropagationData : ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => OperationLog.Current == null ? ActivitySamplingResult.PropagationData : ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                if (OperationLog.Current is { } scope)
                {
                    scope.State.StageStarted();
                    activity.SetCustomProperty("Sengoku.Operation", (scope.State, scope.Id));
                }
            },
            ActivityStopped = activity =>
            {
                if (activity.GetCustomProperty("Sengoku.Operation") is not ValueTuple<OperationState, string> captured) return;
                var dependency = activity.Source.Name == "Npgsql" ? "PostgreSQL" : activity.Source.Name == "System.Net.Http" ? "Http" : "ServiceBus";
                var statusCode = activity.GetTagItem("http.response.status_code") ?? activity.GetTagItem("http.status_code");
                var failed = activity.Status == ActivityStatusCode.Error || (int.TryParse(statusCode?.ToString(), out var code) && code >= 400);
                captured.Item1.AddStage(new(0, activity.SpanId.ToHexString(), captured.Item2, "Dependency." + dependency,
                    new DateTimeOffset(activity.StartTimeUtc), new DateTimeOffset(activity.StartTimeUtc + activity.Duration), activity.Duration.TotalMilliseconds,
                    failed ? "Failed" : "Succeeded", captured.Item1.Policy.Sanitize(OperationFields.Of(("Dependency", dependency), ("HttpStatusCode", statusCode)))));
            }
        };
        ActivitySource.AddActivityListener(listener);
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) { Dispose(); return Task.CompletedTask; }
    public void Dispose() => listener?.Dispose();
}

/// <summary>Existing warnings/errors become safe stage markers; never invoke a message formatter or read logging state.</summary>
public sealed class OperationMarkerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Marker(categoryName);
    public void Dispose() { }
    private sealed class Marker(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning && category.StartsWith("Sengoku", StringComparison.Ordinal) && OperationLog.Current != null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            using var step = OperationLog.Step("Log." + level, OperationFields.Of(("Category", category), ("EventCode", eventId.Id)));
            if (exception != null && level >= LogLevel.Error) step.Fail(exception);
            else if (level >= LogLevel.Error) step.Reject("LoggedError");
            else step.Complete();
        }
    }
}

public static class OperationLoggingConfiguration
{
    public static IServiceCollection AddOperationLogging(this IServiceCollection services, Action<OperationDataPolicy>? configure = null)
    {
        services.TryAddSingleton(_ => { var policy = new OperationDataPolicy(); configure?.Invoke(policy); return policy; });
        services.TryAddSingleton<IOperationSink, AzureOperationSink>();
        services.TryAddSingleton<OperationLogger>();
        services.TryAddSingleton<OperationResultPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, OperationMarkerProvider>());
        services.AddHostedService<OperationDiagnostics>();
        return services;
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sengoku.Observability;

public sealed record OperationStage(long Sequence, string StageId, string? ParentStageId, string Title,
    DateTimeOffset StartTime, DateTimeOffset EndTime, double DurationMs, string Status, IReadOnlyDictionary<string, string> Data);

public sealed record OperationPackage(int SchemaVersion, string OperationId, string TraceId, string SpanId, string? ParentSpanId,
    string Title, DateTimeOffset StartTime, DateTimeOffset? EndTime, double DurationMs, string Status,
    int PartIndex, bool IsFinal, long StageCount, long FailedStageCount, IReadOnlyDictionary<string, string> Data,
    IReadOnlyList<OperationStage> Stages);

public interface IOperationSink { void Write(OperationPackage package); }

public sealed class OperationLogger(IOperationSink sink, OperationDataPolicy policy)
{
    public OperationScope Begin(string title, IEnumerable<KeyValuePair<string, object?>>? data = null, string? traceParent = null, bool forceNew = false)
    {
        if (!forceNew && traceParent == null && OperationLog.Current is { State.IsFinished: false }) return OperationLog.Step(title, data);
        var previousActivity = Activity.Current;
        var activity = new Activity(OperationDataPolicy.Label(title)).SetIdFormat(ActivityIdFormat.W3C);
        if (ActivityContext.TryParse(traceParent, null, out var parent))
            activity.SetParentId(parent.TraceId, parent.SpanId, ActivityTraceFlags.Recorded);
        activity.Start();
        // These packages track every execution, even when an incoming trace was not sampled.
        activity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
        var state = new OperationState(sink, policy, title, activity);
        state.AddData(data);
        return OperationLog.Enter(state, title, null, activity, previousActivity);
    }
    public async Task RunAsync(string title, Func<Task> action, IEnumerable<KeyValuePair<string, object?>>? data = null, bool forceNew = false)
    {
        using var operation = Begin(title, data, forceNew: forceNew);
        try { await action(); operation.Complete(); }
        catch (Exception ex) { operation.Fail(ex); throw; }
    }
}

/// <summary>Ambient state flows through async calls; independent requests remain isolated.</summary>
public static class OperationLog
{
    private static readonly AsyncLocal<OperationScope?> ambient = new();
    public static OperationScope? Current => ambient.Value;
    public static OperationScope Step(string title, IEnumerable<KeyValuePair<string, object?>>? data = null)
    {
        if (Current is not { } parent || parent.State.IsFinished) return new OperationScope();
        var scope = Enter(parent.State, title, parent, null, null);
        scope.Add(data);
        return scope;
    }
    internal static OperationScope Enter(OperationState state, string title, OperationScope? parent, Activity? activity, Activity? previous)
    {
        var scope = new OperationScope(state, title, parent, activity, previous, ambient.Value);
        if (parent != null) state.StageStarted();
        ambient.Value = scope;
        return scope;
    }
    internal static void Exit(OperationScope scope, OperationScope? parent)
    {
        if (ReferenceEquals(ambient.Value, scope)) ambient.Value = parent;
    }
    public static void Data(params (string Name, object? Value)[] data) => Current?.Add(OperationFields.Of(data));
    public static void Identity(params (string Name, object? Value)[] data) => Current?.State.AddData(OperationFields.Of(data));
    public static void Failure(Exception exception)
    {
        using var step = Step("HandledFailure");
        step.Fail(exception);
    }
    public static void Event(string title, params (string Name, object? Value)[] data)
    {
        using var step = Step(title, OperationFields.Of(data));
        step.Complete();
    }
    public static async Task<T> RunAsync<T>(string title, Func<Task<T>> action)
    {
        using var step = Step(title);
        try { var result = await action(); step.Complete(); return result; }
        catch (Exception ex) { step.Fail(ex); throw; }
    }
    public static T Run<T>(string title, Func<T> action)
    {
        using var step = Step(title);
        try { var result = action(); step.Complete(); return result; }
        catch (Exception ex) { step.Fail(ex); throw; }
    }
    public static async Task RunAsync(string title, Func<Task> action)
    {
        using var step = Step(title);
        try { await action(); step.Complete(); }
        catch (Exception ex) { step.Fail(ex); throw; }
    }
}

public sealed class OperationScope : IDisposable
{
    private readonly OperationScope? parent;
    private readonly OperationScope? restore;
    private readonly Activity? activity;
    private readonly Activity? previousActivity;
    private readonly DateTimeOffset start = DateTimeOffset.UtcNow;
    private readonly long stopwatch = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, string> data = new();
    private readonly string title = "Unspecified";
    private string status = "Incomplete";
    private int disposed;
    internal OperationState State { get; } = null!;
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal OperationScope() { }
    internal OperationScope(OperationState state, string title, OperationScope? parent, Activity? activity, Activity? previous, OperationScope? restore)
    {
        State = state; this.title = OperationDataPolicy.Label(title); this.parent = parent;
        this.activity = activity; previousActivity = previous;
        this.restore = restore;
        if (activity != null) Id = activity.SpanId.ToHexString();
    }
    public void Add(IEnumerable<KeyValuePair<string, object?>>? values)
    {
        if (State == null) return;
        lock (data) OperationDataPolicy.Merge(data, State.Policy.Sanitize(values));
    }
    public void Complete() { if (status == "Incomplete") status = "Succeeded"; }
    public void Pending() { if (status == "Incomplete") status = "Pending"; }
    internal void AddObject(object? value)
    {
        if (State != null) lock (data) OperationDataPolicy.Merge(data, State.Policy.Project(value));
    }
    public void Reject(string reasonCode) { status = "Failed"; Add(OperationFields.Of(("ReasonCode", reasonCode))); }
    public void Cancel() => status = "Cancelled";
    public void Fail(Exception exception)
    {
        status = exception is OperationCanceledException ? "Cancelled" : "Failed";
        // Never read Message, Data, ToString(), SQL text, or third-party response bodies.
        var cause = exception.GetBaseException();
        var site = new StackTrace(cause, false).GetFrames()?.FirstOrDefault(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("Sengoku", StringComparison.Ordinal) == true)?.GetMethod();
        var siteName = site == null ? null : (site.DeclaringType?.Name + "." + site.Name).Replace('<', '_').Replace('>', '_').Replace('+', '.');
        Add(OperationFields.Of(("ErrorType", cause.GetType().FullName), ("ErrorSite", siteName)));
    }
    public void Dispose()
    {
        if (State == null || Interlocked.Exchange(ref disposed, 1) != 0) return;
        OperationLog.Exit(this, restore);
        var elapsed = Stopwatch.GetElapsedTime(stopwatch).TotalMilliseconds;
        Dictionary<string, string> copy;
        lock (data) copy = new(data);
        if (parent != null) State.AddStage(new(0, Id, parent.Id, title, start, DateTimeOffset.UtcNow, elapsed, status, copy));
        else
        {
            State.AddSanitizedData(copy);
            State.Finish(status);
            activity?.Stop();
            Activity.Current = previousActivity;
        }
    }
}

internal sealed class OperationState(IOperationSink sink, OperationDataPolicy policy, string title, Activity activity)
{
    private readonly object gate = new();
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset start = DateTimeOffset.UtcNow;
    private readonly long stopwatch = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, string> data = new();
    private readonly List<OperationStage> stages = new();
    private long count, failures, pending, activeStages;
    private int part, bytes;
    public OperationDataPolicy Policy => policy;
    public bool IsFinished { get; private set; }
    internal void StageStarted() { lock (gate) if (!IsFinished) activeStages++; }

    public void AddData(IEnumerable<KeyValuePair<string, object?>>? values)
    {
        AddSanitizedData(policy.Sanitize(values));
    }
    internal void AddSanitizedData(Dictionary<string, string> values)
    {
        lock (gate) OperationDataPolicy.Merge(data, values);
    }
    public void AddStage(OperationStage stage)
    {
        lock (gate)
        {
            if (IsFinished) return;
            activeStages--;
            stage = stage with { Sequence = ++count };
            if (stage.Status is "Failed" or "Incomplete" or "Cancelled") failures++;
            if (stage.Status == "Pending") pending++;
            var size = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(stage));
            // Each serialized stage is bounded by the scalar policy. Keep each part below
            // the 8 KB property limit as well as the 64 KB telemetry item limit.
            if (stages.Count > 0 && bytes + size > 6000) Emit(false, "InProgress");
            stages.Add(stage); bytes += size;
        }
    }
    public void Finish(string status)
    {
        lock (gate)
        {
            if (IsFinished) return;
            IsFinished = true;
            // A caller that forgets to await a child must not receive a false success summary.
            if (activeStages > 0)
            {
                data["ActiveStageCount"] = activeStages.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (status == "Succeeded") status = "Incomplete";
            }
            Emit(true, status == "Succeeded" ? failures > 0 ? "CompletedWithFailures" : pending > 0 ? "Pending" : status : status);
        }
    }
    private void Emit(bool final, string status)
    {
        var package = new OperationPackage(1, id, activity.TraceId.ToHexString(), activity.SpanId.ToHexString(), activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
            OperationDataPolicy.Label(title), start, final ? DateTimeOffset.UtcNow : null, Stopwatch.GetElapsedTime(stopwatch).TotalMilliseconds,
            status, part++, final, count, failures, new Dictionary<string, string>(data), stages.ToArray());
        try { sink.Write(package); }
        catch { TelemetryHealth.RecordExportFailure(); }
        stages.Clear(); bytes = 0;
    }
}

public static class TelemetryHealth
{
    private static long failedWrites;
    public static long FailedWrites => Interlocked.Read(ref failedWrites);
    internal static void RecordExportFailure() => Interlocked.Increment(ref failedWrites);
}

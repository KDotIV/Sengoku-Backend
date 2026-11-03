using System.Collections.Concurrent;

namespace Sengoku.Observability;

/// <summary>Explicit business-result adapters: an empty query or false predicate is not automatically a failure.</summary>
public sealed class OperationResultPolicy
{
    private readonly ConcurrentDictionary<Type, Action<OperationScope, object>> adapters = new();
    public void Register<T>(Action<OperationScope, T> adapter) => adapters[typeof(T)] = (scope, value) => adapter(scope, (T)value);
    internal void Apply(OperationScope scope, object? result)
    {
        if (result == null || !adapters.TryGetValue(result.GetType(), out var adapter)) return;
        try { adapter(scope, result); }
        catch { TelemetryHealth.RecordExportFailure(); }
    }
}

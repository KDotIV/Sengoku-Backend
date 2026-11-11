using System.Collections.Concurrent;

namespace SengokuDispatcher.Library.Services.Diagnostics;

public sealed class WorkerActivity
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, bool Healthy)> _workers = new();
    public void Success(string name) => _workers[name] = (DateTimeOffset.UtcNow, true);
    public void Failure(string name) => _workers[name] = (DateTimeOffset.UtcNow, false);
    public bool Healthy(string name) => _workers.TryGetValue(name, out var state)
        && state.Healthy && DateTimeOffset.UtcNow - state.At < TimeSpan.FromMinutes(2);
}

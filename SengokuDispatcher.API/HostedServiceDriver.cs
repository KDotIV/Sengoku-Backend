namespace SengokuDispatcher.API;

/// <summary>Owns a library worker and its dependency scope for the host lifetime.</summary>
public sealed class HostedServiceDriver<TWorker>(IServiceScopeFactory scopes) : BackgroundService, IAsyncDisposable
    where TWorker : class, IHostedService
{
    private AsyncServiceScope? _scope;
    private TWorker? _worker;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _scope = scopes.CreateAsyncScope();
        _worker = _scope.Value.ServiceProvider.GetRequiredService<TWorker>();
        await _worker.StartAsync(stoppingToken);
        if (_worker is BackgroundService background && background.ExecuteTask != null)
        {
            // Propagate worker failures to the host's BackgroundServiceExceptionBehavior.
            await background.ExecuteTask;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_worker != null)
        {
            await _worker.StopAsync(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_scope.HasValue)
        {
            await _scope.Value.DisposeAsync();
            _scope = null;
        }
    }
}

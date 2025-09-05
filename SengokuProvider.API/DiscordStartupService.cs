namespace SengokuProvider.API
{
    public sealed class DiscordStartupService : IHostedService
    {
        private readonly ILogger<DiscordStartupService> _log;
        private readonly EventListenerManager _listenerManager;
        public DiscordStartupService(ILogger<DiscordStartupService> logger, EventListenerManager listenerManager)
        {
            _log = logger;
            _listenerManager = listenerManager;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _log.LogInformation("Starting Discord socket initialization…");
                await _listenerManager.InitializeAllAsync(cancellationToken);
                _log.LogInformation("Discord socket initialization completed.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error during Discord socket initialization.");
            }
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

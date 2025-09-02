using Discord.WebSocket;

namespace ExcluSightsLibrary.DiscordServices
{
    public interface ISocketEngine
    {
        public Task EnsureStartedAsync();
        public Task<bool> WaitForInitialBackfillAsync(TimeSpan timeout);
    }
}
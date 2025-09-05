namespace ExcluSightsLibrary.DiscordServices
{
    public interface ISocketEngine
    {
        public Task EnsureStartedAsync();
        IReadOnlyList<(ulong GuildId, string GuildName)> GetConnectedGuilds();
        public Task<bool> WaitForInitialBackfillAsync(TimeSpan timeout);
        Task<bool> DownloadGuildMembersAsync(ulong guildId);
    }
}
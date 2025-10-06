using ExcluSightsLibrary.DiscordModels;

namespace ExcluSightsLibrary.DiscordServices
{
    public interface ISocketEngine
    {
        Task EnsureStartedAsync();
        IReadOnlyList<(ulong GuildId, string GuildName)> GetConnectedGuilds();
        Task<bool> WaitForInitialBackfillAsync(TimeSpan timeout);
        Task<bool> DownloadGuildMembersAsync(ulong guildId);
        IReadOnlyList<DiscordRoleData> GetRolesForConnectedGuild(ulong guildId);
        Task<IReadOnlyList<CustomerProfileData>> GetCustomersDataByGuildIdAsync(ulong guildId);
    }
}
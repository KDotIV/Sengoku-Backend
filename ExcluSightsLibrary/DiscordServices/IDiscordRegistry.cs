using ExcluSightsLibrary.DiscordModels;

namespace ExcluSightsLibrary.DiscordServices
{
    public interface IDiscordRegistry
    {
        IEnumerable<DiscordGuildData> GetAllRegisteredGuilds();
        bool TryGetGuildData(ulong guildId, out DiscordGuildData? guildData);
        void Upsert(DiscordGuildData guildData);
        void Remove(ulong guildId);
    }
}
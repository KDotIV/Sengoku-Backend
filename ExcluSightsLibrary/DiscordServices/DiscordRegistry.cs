using ExcluSightsLibrary.DiscordModels;
using System.Collections.Concurrent;

namespace ExcluSightsLibrary.DiscordServices
{
    public sealed class DiscordRegistry : IDiscordRegistry
    {
        private readonly ConcurrentDictionary<ulong, DiscordGuildData> _map = new();
        public IEnumerable<DiscordGuildData> GetAllRegisteredGuilds() => _map.Values;
        public bool TryGetGuildData(ulong guildId, out DiscordGuildData data) => _map.TryGetValue(guildId, out data!);
        public void Upsert(DiscordGuildData data) => _map.AddOrUpdate(data.ServerGuildId, data, (_, __) => data);
        public void Remove(ulong guildId) => _map.TryRemove(guildId, out _);
    }
}

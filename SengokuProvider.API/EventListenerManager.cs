using ExcluSightsLibrary.DiscordModels;
using ExcluSightsLibrary.DiscordServices;

namespace SengokuProvider.API
{
    public sealed class EventListenerManager
    {
        private readonly ILogger<EventListenerManager> _log;
        private readonly ISocketEngine _socketEngine;
        private readonly IDiscordRegistry _discordRegistry;
        public EventListenerManager(ILogger<EventListenerManager> logger, ISocketEngine socketEngine, IDiscordRegistry registry)
        {
            _log = logger;
            _socketEngine = socketEngine;
            _discordRegistry = registry;
        }
        // Initialize socket and backfill everything (used at startup)
        public async Task InitializeAllAsync(CancellationToken ct = default)
        {
            _log.LogInformation("Initializing Discord socket…");
            await _socketEngine.EnsureStartedAsync();
            await _socketEngine.WaitForInitialBackfillAsync(TimeSpan.FromSeconds(50));

            foreach (var (id, name) in _socketEngine.GetConnectedGuilds())
            {
                _discordRegistry.Upsert(new DiscordGuildData
                {
                    ServerGuildId = id,
                    ServerName = name,
                    Roles = new List<DiscordRoleData>(),
                    LastUpdatedUTC = DateTimeOffset.UtcNow
                });
            }
            _log.LogInformation("Socket ready. Guilds: {Count}", "");
        }
        public async Task<bool> AddDiscordWebSocketListener(ulong guildId, CancellationToken ct = default)
        {
            await _socketEngine.EnsureStartedAsync();

            var ok = await _socketEngine.DownloadGuildMembersAsync(guildId);
            if (!ok)
            {
                _log.LogWarning("Bot is not in guild {GuildId}", guildId);
                return false;
            }

            // refresh registry entry
            var guild = _socketEngine.GetConnectedGuilds().FirstOrDefault(g => g.GuildId == guildId);
            if (guild.GuildId == 0) return false;

            _discordRegistry.Upsert(new DiscordGuildData
            {
                ServerGuildId = guild.GuildId,
                ServerName = guild.GuildName,
                LastUpdatedUTC = DateTimeOffset.UtcNow
            });

            // Optional: trigger DB “scan-and-upsert” for all current members here.
            // This is where you’d enumerate guild users and upsert users/roles.
            // Consider streaming via Channels to a background writer if large.

            return true;
        }
        public async Task<List<DiscordRoleData>> GetGuildRoles(ulong guildId, CancellationToken ct = default)
        {
            if (guildId <= 0) throw new ArgumentOutOfRangeException(nameof(guildId));

            var rolesResult = new List<DiscordRoleData>();

            await _socketEngine.EnsureStartedAsync();

            var ok = await _socketEngine.DownloadGuildMembersAsync(guildId);
            if (!ok)
            {
                _log.LogWarning("Bot is not in guild {GuildId}", guildId);
                return rolesResult;
            }

            rolesResult = _socketEngine.GetRolesForConnectedGuild(guildId).ToList();
            return rolesResult;
        }
        public async Task<IReadOnlyList<CustomerProfileData>> GetCustomersDataByGuildId(ulong guildId)
        {
            if (guildId <= 0) throw new ArgumentOutOfRangeException(nameof(guildId));
            await _socketEngine.EnsureStartedAsync();
            var ok = await _socketEngine.DownloadGuildMembersAsync(guildId);
            if (!ok)
            {
                _log.LogWarning("Bot is not in guild {GuildId}", guildId);
                return Array.Empty<CustomerProfileData>();
            }
            var customers = await _socketEngine.GetCustomersDataByGuildIdAsync(guildId);
            return customers;
        }
    }
}

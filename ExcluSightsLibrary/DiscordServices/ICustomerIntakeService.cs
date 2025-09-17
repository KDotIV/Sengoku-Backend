using Discord.WebSocket;
using ExcluSightsLibrary.DiscordModels;

namespace ExcluSightsLibrary.DiscordServices
{
    public interface ICustomerIntakeService
    {
        // audits
        Task<IReadOnlyList<int>> ApplyRoleDiffAsync(ulong guildId, ulong discordId, IEnumerable<long> added, IEnumerable<long> removed, CancellationToken ct = default);

        // customer linkage
        Task<bool> UpsertCustomerAsync(CustomerProfileData model, CancellationToken ct = default);
        Task<bool> UpsertDiscordAccountAsync(ulong discordId, string discordTag, string? discriminator, string customerId, CancellationToken ct = default);
        Task UpsertMembershipAsync(ulong guildId, ulong discordId, DateTimeOffset? joinedAtUtc = null);
        Task<string> GenerateNewCustomerID(CancellationToken ct = default);

        // per-guild derivations from roles
        Task UpsertSolePlayFromRolesAsync(ulong guildId, ulong discordId, CancellationToken ct = default);
        Task SyncCustomerInterestsFromRolesAsync(ulong guildId, ulong discordId, CancellationToken ct = default);
        public Task<int> SaveGuildMemberChangesAsync(List<SolePlayDTO> reducedMembers, CancellationToken ct = default);
        Task UpsertGuildRoleChangesAsync(ulong id, IReadOnlyCollection<SocketRole> roles);
    }
}
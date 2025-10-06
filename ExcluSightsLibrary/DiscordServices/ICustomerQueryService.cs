using ExcluSightsLibrary.DiscordModels;

namespace ExcluSightsLibrary.DiscordServices
{
    public interface ICustomerQueryService
    {
        Task<(string customer_id, int gender, decimal shoe_size)> VerifyCustomerFromRoleMap(ulong guildId, ulong discordId);
        Task<List<RoleMapping>> GetServerRoleWhiteList(ulong guildId);
        Task<SolePlayDTO?> GetCustomerByID(string customerId, CancellationToken ct = default);
        Task<IReadOnlyList<CustomerProfileData>> GetCustomersDataByGuildId(ulong guildId);
        Task<SolePlayDTO?> GetCustomerByDiscordId(ulong discordId, CancellationToken ct = default);
    }
}
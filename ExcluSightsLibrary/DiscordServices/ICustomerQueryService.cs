using ExcluSightsLibrary.DiscordModels;

namespace ExcluSightsLibrary.DiscordServices
{
    public interface ICustomerQueryService
    {
        Task<(string customer_id, int gender, decimal shoe_size)> VerifyCustomerFromRoleMap(ulong guildId, ulong discordId);
        Task<List<RoleMapping>> GetServerRoleWhiteList(ulong guildId);
        Task<SolePlayDTO?> GetCustomerByID(string customerId);
        Task<IReadOnlyList<CustomerProfileData>> GetCustomersDataByGuildId(ulong guildId, string? email);
    }
}
using ExcluSightsLibrary.DiscordModels;

namespace ExcluSightsLibrary.DiscordServices
{
    public interface ICustomerQueryService
    {
        public Task<(string customer_id, int gender, decimal shoe_size)> VerifyCustomerFromRoleMap(ulong guildId, ulong discordId);
        public Task<List<RoleMapping>> GetServerRoleWhiteList(ulong guildId);
        public Task<SolePlayDTO?> GetCustomerByID(string customerId);
    }
}
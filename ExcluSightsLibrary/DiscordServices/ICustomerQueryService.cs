namespace ExcluSightsLibrary.DiscordServices
{
    public interface ICustomerQueryService
    {
        public Task<(string customer_id, int gender, decimal shoe_size)> VerifyCustomerFromRoleMap(ulong guildId, ulong discordId);
        public Task<List<(ulong guildId, ulong roleId, string roleName)>> GetServerRoleWhiteList(ulong guildId);
    }
}
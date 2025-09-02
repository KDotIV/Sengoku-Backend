using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExcluSightsLibrary.DiscordServices
{
    public class CustomerQueryService : ICustomerQueryService
    {
        private readonly string _connectionString;
        private readonly ILogger<ICustomerQueryService> _log;

        public CustomerQueryService(string connectionString, ILogger<ICustomerQueryService> logger)
        {
            _connectionString = connectionString;
            _log = logger;
        }

        public async Task<(string customer_id, int gender, decimal shoe_size)> VerifyCustomerFromRoleMap(ulong guildId, ulong discordId)
        {
            if (guildId <= 0 || discordId <= 0) throw new ArgumentOutOfRangeException(nameof(guildId));

            try
            {
                const string sql = @"
                    WITH mapped AS (
                      SELECT rmp.gender, rmp.shoe_size
                      FROM discord_user_roles ur
                      JOIN role_map_profile rmp
                        ON rmp.guild_id = @gid
                       AND rmp.role_id  = ur.role_id
                     WHERE ur.guild_id  = @gid
                       AND ur.discord_id= @did
                    ),
                    acc AS (
                      SELECT customer_id FROM discord_accounts WHERE discord_id=@did
                    )
                    SELECT
                      (SELECT customer_id FROM acc) AS customer_id,
                      (SELECT MAX(gender)    FROM mapped WHERE gender    IS NOT NULL) AS gender,
                      (SELECT MAX(shoe_size) FROM mapped WHERE shoe_size IS NOT NULL) AS shoe_size;";

                using var conn = CreateConnection();
                await conn.OpenAsync();
                (string customer_id, int gender, decimal shoe_size) row = await conn.QuerySingleOrDefaultAsync<(string customer_id, int gender, decimal shoe_size)>(
                    sql, new { gid = (long)guildId, did = (long)discordId });

                if (row.customer_id is not null) return row;
                else return ("", 0, 0); // no customer found
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "VerifyCustomerFromRoleMap failed for Guild: {GuildId}, User: {DiscordId}", guildId, discordId);
                throw;
            }
        }
        private NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);
    }
}

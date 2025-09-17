using Dapper;
using ExcluSightsLibrary.DiscordModels;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExcluSightsLibrary.DiscordServices
{
    public class CustomerQueryService : ICustomerQueryService
    {
        private readonly string _connectionString;
        private readonly ILogger<ICustomerQueryService> _log;
        private static readonly SemaphoreSlim DbGate = new(initialCount: 8, maxCount: 8); // limit to 8 concurrent DB operations
        private readonly NpgsqlDataSource _dataSource;
        public CustomerQueryService(NpgsqlDataSource dataSource, ILogger<ICustomerQueryService> logger)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _log = logger;
        }
        private async Task<T> WithDbGate<T>(Func<Task<T>> operation)
        {
            await DbGate.WaitAsync();
            try
            {
                return await operation();
            }
            finally
            {
                DbGate.Release();
            }
        }
        private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default) => await _dataSource.OpenConnectionAsync(ct);
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

                await using var conn = await OpenAsync();
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
        public async Task<List<RoleMapping>> GetServerRoleWhiteList(ulong guildId)
        {
            if (guildId <= 0) throw new ArgumentOutOfRangeException(nameof(guildId));
            try
            {
                var results = new List<RoleMapping>();
                const string sql = @"SELECT * FROM role_map_profile WHERE guild_id = @gid;";
                await using var conn = await OpenAsync();

                using var cmd = new NpgsqlCommand(sql, conn)
                {
                    Parameters =
                    {
                        new NpgsqlParameter("gid", (long)guildId)
                    }
                };

                using var reader = await cmd.ExecuteReaderAsync();
                if (!reader.HasRows)
                {
                    Console.WriteLine("No rows found.");
                    return results; // no roles found
                }
                while (await reader.ReadAsync())
                {
                    results.Add(new RoleMapping(
                        GuildId: (ulong)reader.GetInt64(0),
                        RoleId: (ulong)reader.GetInt64(1),
                        RoleName: reader.GetString(2)
                        ));
                }

                return results;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GetServerWhiteList failed for Guild: {GuildId}", guildId);
                throw;
            }
        }

        public async Task<SolePlayDTO?> GetCustomerByID(string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId)) throw new ArgumentNullException(nameof(customerId));

            try
            {
                await using var conn = await OpenAsync();

                const string query = @"SELECT * FROM customer_profile_soleplay WHERE customer_id = @cid LIMIT 1;";
                var result = await conn.QuerySingleOrDefaultAsync<SolePlayDTO>(query, new { cid = customerId });

                return result ?? null;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
                return null;
            }
        }
    }
}

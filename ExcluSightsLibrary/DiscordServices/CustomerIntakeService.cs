using Dapper;
using ExcluSightsLibrary.DiscordModels;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExcluSightsLibrary.DiscordServices
{
    public class CustomerIntakeService : ICustomerIntakeService
    {
        private readonly string _connectionString;
        private readonly ILogger<ICustomerIntakeService> _log;

        private readonly ICustomerQueryService _customerQuery;
        public CustomerIntakeService(string connectionString, ILogger<ICustomerIntakeService> logger, ICustomerQueryService customerQuery)
        {
            _connectionString = connectionString;
            _log = logger;
            _customerQuery = customerQuery;
        }

        public async Task<IReadOnlyList<int>> ApplyRoleDiffAsync(ulong guildId, ulong discordId, IEnumerable<long> added, IEnumerable<long> removed, CancellationToken ct = default)
        {
            var auditIds = new List<int>(8);

            const string addSql = @"
                INSERT INTO discord_user_roles (guild_id, discord_id, role_id)
                VALUES (@gid, @did, @rid) ON CONFLICT DO NOTHING;
                INSERT INTO user_role_audit (guild_id, discord_id, role_id, action)
                VALUES (@gid, @did, @rid, 'added')
                RETURNING id;";

            const string remSql = @"
                DELETE FROM discord_user_roles WHERE guild_id=@gid AND discord_id=@did AND role_id=@rid;
                INSERT INTO user_role_audit (guild_id, discord_id, role_id, action)
                VALUES (@gid, @did, @rid, 'removed')
                RETURNING id;";

            using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                foreach (var r in added)
                {
                    var id = await conn.ExecuteScalarAsync<int?>(addSql, new { gid = (long)guildId, did = (long)discordId, rid = r }, tx);
                    if (id.HasValue) auditIds.Add(id.Value);
                }
                foreach (var r in removed)
                {
                    var id = await conn.ExecuteScalarAsync<int?>(remSql, new { gid = (long)guildId, did = (long)discordId, rid = r }, tx);
                    if (id.HasValue) auditIds.Add(id.Value);
                }

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _log.LogError(ex, "ApplyRoleDiff failed for guild {GuildId}, user {DiscordId}", guildId, discordId);
                throw;
            }

            return auditIds;
        }
        public async Task SyncCustomerInterestsFromRolesAsync(ulong guildId, ulong discordId, CancellationToken ct = default)
        {
            // Derive interest_ids from *this guildId* roles
            const string sql = @"
                WITH derived AS (
                  SELECT DISTINCT rmi.interest_id
                  FROM discord_user_roles ur
                  JOIN role_map_interest rmi
                    ON rmi.guild_id = @gid
                   AND rmi.role_id  = ur.role_id
                 WHERE ur.guild_id  = @gid
                   AND ur.discord_id= @did
                ),
                acc AS (
                  SELECT customer_id FROM discord_accounts WHERE discord_id=@did
                )
                SELECT (SELECT customer_id FROM acc) AS customer_id,
                       ARRAY(SELECT interest_id FROM derived ORDER BY interest_id) AS interest_ids;";

            using var conn = CreateConnection();
            var x = await conn.QuerySingleOrDefaultAsync<(string? customer_id, int[]? interest_ids)>(
                sql, new { gid = (long)guildId, did = (long)discordId });

            if (x.customer_id is null) return;

            var cid = x.customer_id!;
            var derived = x.interest_ids ?? Array.Empty<int>();

            // existing across ALL sources for this customer (we only touch 'discord' rows)
            const string getExisting = @"SELECT interest_id FROM customer_interests WHERE customer_id=@cid AND source='discord';";
            var existing = (await conn.QueryAsync<int>(getExisting, new { cid })).ToHashSet();

            var toAdd = derived.Except(existing).ToArray();
            var toRem = existing.Except(derived).ToArray();

            if (toAdd.Length == 0 && toRem.Length == 0) return;

            const string insertSql = @"
                INSERT INTO customer_interests (customer_id, interest_id, source)
                VALUES (@cid, @iid, 'discord')
                ON CONFLICT DO NOTHING;
                INSERT INTO customer_interest_audit (customer_id, interest_id, action, source)
                VALUES (@cid, @iid, 'added', 'discord');";

            const string deleteSql = @"
                DELETE FROM customer_interests WHERE customer_id=@cid AND interest_id=@iid AND source='discord';
                INSERT INTO customer_interest_audit (customer_id, interest_id, action, source)
                VALUES (@cid, @iid, 'removed', 'discord');";

            using var conn2 = CreateConnection();
            await conn2.OpenAsync(ct);
            using var tx = await conn2.BeginTransactionAsync(ct);

            try
            {
                foreach (var iid in toAdd)
                    await conn2.ExecuteAsync(insertSql, new { cid, iid }, tx);

                foreach (var iid in toRem)
                    await conn2.ExecuteAsync(deleteSql, new { cid, iid }, tx);

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _log.LogError(ex, "SyncCustomerInterestsFromRoles failed for guild {GuildId}, user {DiscordId}", guildId, discordId);
                throw;
            }
        }
        public async Task<bool> UpsertCustomerAsync(CustomerProfileData model, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(model.CustomerId))
                throw new ArgumentException("Must have valid CustomerId", nameof(model.CustomerId));

            if (model.DiscordId == 0) throw new ArgumentException("Must have valid DiscordId", nameof(model.DiscordId));

            const string sqlCustomer = @"
                INSERT INTO customers (customer_id, first_name, last_name)
                VALUES (@cid, @fn, @ln)
                ON CONFLICT (customer_id) DO UPDATE SET
                  first_name = EXCLUDED.first_name,
                  last_name  = EXCLUDED.last_name,
                  updated_at = now();";

            const string sqlLink = @"
                UPDATE discord_accounts
                   SET customer_id = @cid, updated_at = now()
                 WHERE discord_id  = @did;";

            using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                var insertResult = await conn.ExecuteAsync(sqlCustomer, new
                {
                    cid = model.CustomerId,
                    fn = (object?)model.CustomerFirstName ?? DBNull.Value,
                    ln = (object?)model.CustomerLastName ?? DBNull.Value
                }, tx);

                if (insertResult <= 0)
                {
                    _log.LogWarning("UpsertCustomer did not affect any rows for CustomerId {CustomerId}", model.CustomerId);
                    return false;
                    //TODO put retry logic
                }
                var updateResult = await conn.ExecuteAsync(sqlLink, new
                {
                    cid = model.CustomerId,
                    did = (long)model.DiscordId
                }, tx);

                if (updateResult <= 0)
                {
                    _log.LogWarning("UpsertCustomer did not link any rows for CustomerId {CustomerId}, DiscordId {DiscordId}", model.CustomerId, model.DiscordId);
                    return false;
                }

                _log.LogInformation("Successful Upserted Customer: CustomerId: {CustomerId}, DiscordId: {DiscordId}", model.CustomerId, model.DiscordId);
                await tx.CommitAsync(ct);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                _log.LogError("UpsertCustomer failed for CustomerId {CustomerId}, DiscordId {DiscordId}", model.CustomerId, model.DiscordId);
                return false;
            }
        }
        public async Task<bool> UpsertDiscordAccountAsync(ulong discordId, string discordTag, string? discriminator, string customerId)
        {
            if (string.IsNullOrEmpty(customerId) || string.IsNullOrEmpty(discordTag))
                throw new ArgumentException("DiscordTag or CustomerId cannot be null or whitespace.", nameof(customerId));
            if (discordId == 0)
                throw new ArgumentException("Need a valid DiscordID.", nameof(discordId));

            try
            {
                const string sql = @"
                INSERT INTO discord_accounts (discord_id, discord_tag, username, discriminator, customer_id)
                VALUES (@did, @tag, @disc, @cust)
                ON CONFLICT (discord_id) DO UPDATE SET
                  discord_tag  = EXCLUDED.discord_tag,
                  username     = EXCLUDED.username,
                  discriminator= EXCLUDED.discriminator,
                  updated_at   = now();";

                using var conn = CreateConnection();
                await conn.OpenAsync();
                var result = await conn.ExecuteAsync(sql, new
                {
                    did = (long)discordId,
                    tag = discordTag,
                    disc = discriminator ?? string.Empty,
                    cust = customerId,
                });
                if (result > 0)
                {
                    _log.LogInformation("Successful Upserted Discord Account: DiscordId: {DiscordId}, CustomerId: {CustomerId}", discordId, customerId);
                    return true;
                }
                else
                {
                    _log.LogWarning("No rows affected when upserting Discord Account: DiscordId: {DiscordId}, CustomerId: {CustomerId}", discordId, customerId);
                    return false;
                }
            }
            catch (NpgsqlException ex)
            {
                _log.LogError(ex, "Error occured while saving Discord Account: NpgsqlException for DiscordId: {DiscordId}, CustomerId: {CustomerId}", discordId, customerId);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error occured while saving Discord Account: Exception for DiscordId: {DiscordId}, CustomerId: {CustomerId}", discordId, customerId);
                throw;
            }
        }
        public async Task UpsertMembershipAsync(ulong guildId, ulong discordId, DateTimeOffset? joinedAtUtc = null)
        {
            if (guildId <= 0 || discordId <= 0)
                throw new ArgumentException("Need a valid GuildID and DiscordID.", nameof(guildId));
            try
            {
                const string sql = @"
                    INSERT INTO discord_memberships (guild_id, discord_id, joined_at)
                    VALUES (@gid, @did, @joined)
                    ON CONFLICT (guild_id, discord_id) DO NOTHING;";
                using var conn = CreateConnection();
                await conn.OpenAsync();
                var result = await conn.ExecuteAsync(sql, new
                {
                    gid = (long)guildId,
                    did = (long)discordId,
                    joined = (object?)joinedAtUtc?.UtcDateTime ?? DBNull.Value
                });
                if (result > 0)
                    _log.LogInformation("Successful Upserted Discord Membership: DiscordId: {DiscordId}, GuildID: {GuildId}", discordId, guildId);
                else
                    _log.LogInformation("No rows affected when upserting Discord Membership (likely already exists): DiscordId: {DiscordId}, GuildID: {GuildId}", discordId, guildId);

            }
            catch (NpgsqlException ex)
            {
                _log.LogError(ex, "Error occured while saving Discord Membership: NpgsqlException for DiscordId: {DiscordId}, GuildID: {GuildId}", discordId, guildId);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error occured while saving Discord Membership: Exception for DiscordId: {DiscordId}, GuildID: {GuildId}", discordId, guildId);
                throw;
            }
        }
        public async Task UpsertSolePlayFromRolesAsync(ulong guildId, ulong discordId, CancellationToken ct = default)
        {
            if (guildId == 0 || discordId == 0)
                throw new ArgumentException("Need a valid GuildID and DiscordID.", nameof(guildId));

            try
            {
                var conn = CreateConnection();
                await conn.OpenAsync(ct);

                var row = await _customerQuery.VerifyCustomerFromRoleMap(guildId, discordId);
                if (string.IsNullOrEmpty(row.customer_id))
                {
                    _log.LogWarning("Couldn't find customers for GuildId {GuildId}, DiscordId {DiscordId}", guildId, discordId);
                    return;
                }

                const string upsert = @"
                    INSERT INTO customer_profile_soleplay (customer_id, discord_id, shoe_size, gender, updated_at)
                    VALUES (@cid, @did, @shoe, @gender, now())
                    ON CONFLICT (customer_id) DO UPDATE SET
                      discord_id = EXCLUDED.discord_id,
                      shoe_size  = EXCLUDED.shoe_size,
                      gender     = EXCLUDED.gender,
                      updated_at = now();";

                await conn.ExecuteAsync(upsert, new
                {
                    cid = row.customer_id!,
                    did = discordId!,
                    shoe = row.shoe_size,
                    row.gender
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "UpsertSolePlayFromRoles failed for GuildId {GuildId}, DiscordId {DiscordId}", guildId, discordId);
                throw;
            }
        }
        private NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);
    }
}

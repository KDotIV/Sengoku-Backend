using Dapper;
using Discord.WebSocket;
using ExcluSightsLibrary.DiscordModels;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Text;

namespace ExcluSightsLibrary.DiscordServices
{
    public class CustomerIntakeService : ICustomerIntakeService
    {
        //private readonly string _connectionString;
        private static readonly SemaphoreSlim DbGate = new(initialCount: 8, maxCount: 8); // limit to 8 concurrent DB operations
        private readonly ILogger<ICustomerIntakeService> _log;
        private readonly NpgsqlDataSource _dataSource;
        private readonly ICustomerQueryService _customerQuery;
        private static Random _rand = new Random();
        public CustomerIntakeService(NpgsqlDataSource dataSource, ILogger<ICustomerIntakeService> logger, ICustomerQueryService customerQuery)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _log = logger;
            _customerQuery = customerQuery;
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

            await WithDbGate(async () =>
            {
                await using var conn = await OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

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
            });
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

            try
            {
                await using var conn = await OpenAsync(ct);
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

                await using var conn2 = await OpenAsync(ct);
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
            catch (Exception ex)
            {
                _log.LogError(ex, "SyncCustomerInterestsFromRoles failed for guild {GuildId}, user {DiscordId}", guildId, discordId);
                throw;
            }
        }
        public async Task<bool> UpsertCustomerAsync(CustomerProfileData model, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(model.CustomerId))
                throw new ArgumentException("Must have valid CustomerId", nameof(model.CustomerId));

            if (model.DiscordId == 0) throw new ArgumentException("Must have valid DiscordId", nameof(model.DiscordId));

            const string sqlDetachDiscord = @"
                UPDATE customers
                   SET discord_id = NULL,
                       updated_at = now()
                 WHERE discord_id  = @did
                   AND customer_id <> @cid;";

            const string sqlCustomer = @"
                INSERT INTO customers (customer_id, first_name, last_name, discord_id)
                VALUES (@cid, @fn, @ln, @did)
                ON CONFLICT (customer_id) DO UPDATE SET
                  discord_id = EXCLUDED.discord_id,
                  first_name = EXCLUDED.first_name,
                  last_name  = EXCLUDED.last_name,
                  updated_at = now();";

            const string sqlLink = @"
                UPDATE discord_accounts
                   SET customer_id = @cid, updated_at = now()
                 WHERE discord_id  = @did;";

            await using var conn = await OpenAsync(ct);
            using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                await conn.ExecuteAsync(sqlDetachDiscord, new
                {
                    cid = model.CustomerId,
                    did = (long)model.DiscordId
                }, tx);

                var insertResult = await conn.ExecuteAsync(sqlCustomer, new
                {
                    cid = model.CustomerId,
                    fn = (object?)model.CustomerFirstName ?? DBNull.Value,
                    ln = (object?)model.CustomerLastName ?? DBNull.Value,
                    did = (long)model.DiscordId
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
        public async Task<bool> UpsertDiscordAccountAsync(ulong discordId, string discordTag, string? discriminator, string customerId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(customerId) || string.IsNullOrEmpty(discordTag))
                throw new ArgumentException("DiscordTag or CustomerId cannot be null or whitespace.", nameof(customerId));
            if (discordId == 0)
                throw new ArgumentException("Need a valid DiscordID.", nameof(discordId));

            try
            {
                const string sql = @"
                INSERT INTO discord_accounts (discord_id, discord_tag, discriminator, customer_id)
                VALUES (@did, @tag, @disc, @cust)
                ON CONFLICT (discord_id) DO UPDATE SET
                  discord_tag  = EXCLUDED.discord_tag,
                  discriminator= EXCLUDED.discriminator,
                  customer_id  = EXCLUDED.customer_id,    
                  updated_at   = now();";

                await using var conn = await OpenAsync(ct);
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

                await using var conn = await OpenAsync();
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
                await using var conn = await OpenAsync(ct);

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
        public async Task<string> GenerateNewCustomerID(CancellationToken ct = default)
        {
            for (var i = 0; i < 5; i++)
            {
                var newId = Random.Shared.Next(100000, 1000000).ToString();

                const string sql = @"INSERT INTO customers (customer_id) VALUES (@cid)
                             ON CONFLICT (customer_id) DO NOTHING;";

                await using var conn = await OpenAsync(ct);
                var rows = await conn.ExecuteAsync(sql, new { cid = newId });
                if (rows > 0) return newId;

                _log.LogWarning("Collision generating CustomerId {CustomerId}, retrying...", newId);
                i++;
            }
            throw new InvalidOperationException("Could not generate unique CustomerId after 5 attempts.");
        }
        public async Task<int> SaveGuildMemberChangesAsync(List<SolePlayDTO> reducedMembers, CancellationToken ct = default)
        {
            if (reducedMembers == null || reducedMembers.Count == 0)
                throw new ArgumentException("reducedMembers cannot be null or empty.", nameof(reducedMembers));

            // Filter out duplicates locally so the bulk upsert cannot violate the
            // unique constraints on customer_id / discord_id.
            var uniqueMembers = new List<SolePlayDTO>(reducedMembers.Count);
            var seenCustomerIds = new HashSet<string>(StringComparer.Ordinal);
            var seenDiscordIds = new HashSet<ulong>();

            for (var i = reducedMembers.Count - 1; i >= 0; i--)
            {
                var member = reducedMembers[i];

                if (!seenCustomerIds.Add(member.CustomerId))
                {
                    Console.WriteLine($"Skipping duplicate CustomerID {member.CustomerId} while preparing customer_profile_soleplay upsert payload.");
                    continue;
                }

                if (!seenDiscordIds.Add(member.DiscordId))
                {
                    Console.WriteLine($"Skipping duplicate DiscordID {member.DiscordId} while preparing customer_profile_soleplay upsert payload.");
                    continue;
                }

                uniqueMembers.Add(member);
            }

            uniqueMembers.Reverse();

            if (uniqueMembers.Count == 0)
            {
                Console.WriteLine("No unique guild member rows to upsert after duplicate filtering.");
                return 0;
            }

            var expectedResult = uniqueMembers.Count;

            await WithDbGate(async () =>
            {
                try
                {
                    await using var conn = await OpenAsync(ct);

                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        var insertSql = new StringBuilder(@"INSERT INTO customer_profile_soleplay (customer_id, discord_id, shoe_size, gender, updated_at) VALUES ");
                        var insertParams = new List<NpgsqlParameter>();
                        var valueCount = 0;

                        foreach (var data in uniqueMembers)
                        {
                            if (!await VerifyCustomer(data))
                            {
                                Console.WriteLine($"Failed to verify CustomerID {data.CustomerId} and DiscordID {data.DiscordId}\nSkipping...");
                                continue;
                            }

                            if (valueCount > 0) insertSql.Append(", ");
                            insertSql.Append($"(@cid{valueCount}, @did{valueCount}, @shoe{valueCount}, @gender{valueCount}, now())");

                            insertParams.Add(new NpgsqlParameter($"cid{valueCount}", data.CustomerId));
                            insertParams.Add(new NpgsqlParameter($"did{valueCount}", (long)data.DiscordId));
                            insertParams.Add(new NpgsqlParameter($"shoe{valueCount}", data.ShoeSize));
                            insertParams.Add(new NpgsqlParameter($"gender{valueCount}", data.Gender));

                            valueCount++;
                        }

                        expectedResult = valueCount;

                        if (valueCount == 0)
                        {
                            Console.WriteLine("No valid guild member rows remained after verification checks; skipping upsert.");
                            await transaction.RollbackAsync(ct);
                            return 0;
                        }

                        insertSql.Append(" ON CONFLICT (customer_id) DO UPDATE SET discord_id = EXCLUDED.discord_id, shoe_size = EXCLUDED.shoe_size, updated_at = EXCLUDED.updated_at;");

                        using (var cmd = new NpgsqlCommand(insertSql.ToString(), conn, transaction))
                        {
                            cmd.Parameters.AddRange(insertParams.ToArray());
                            var rowsAffected = await cmd.ExecuteNonQueryAsync();

                            if (rowsAffected > 0)
                            {
                                Console.WriteLine($"Upserted {rowsAffected} customer profiles.");
                                if (rowsAffected != expectedResult)
                                {
                                    Console.WriteLine($"Warning: Expected to upsert {expectedResult} profiles, but only {rowsAffected} were affected.");
                                    expectedResult = rowsAffected;
                                }
                            }
                        }
                        await transaction.CommitAsync();
                    }
                    return expectedResult;
                }
                catch (NpgsqlException ex)
                {
                    throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
                    return 0;
                }
            });
            return 0;
        }
        private async Task<bool> VerifyCustomer(SolePlayDTO data)
        {
            if (await _customerQuery.GetCustomerByID(data.CustomerId) == null)
            {
                Console.WriteLine($"CustomerID {data.CustomerId} does not exist. Creating Links...");

                var newLink = await UpsertDiscordAccountAsync(data.DiscordId, data.DiscordTag, " ", data.CustomerId);
                if (!newLink) return false;

                Console.WriteLine($"Created DiscordAccount link for CustomerID {data.CustomerId} and DiscordID {data.DiscordId}");
                Console.WriteLine($"Creating Customer Link for: {data.CustomerId}...");

                var newCustomer = await UpsertCustomerAsync(new CustomerProfileData()
                {
                    CustomerId = data.CustomerId,
                    CustomerFirstName = data.CustomerFirstName,
                    CustomerLastName = data.CustomerLastName,
                    DiscordId = data.DiscordId,
                    DiscordTag = data.DiscordTag,
                    LastUpdated = data.LastUpdated,
                });

                if (!newCustomer) return false;
            }
            return true;
        }
        public async Task UpsertGuildRoleChangesAsync(ulong guildId, IReadOnlyCollection<SocketRole> roles)
        {
            if (guildId <= 0)
                throw new ArgumentException("Need a valid GuildID.", nameof(guildId));
            if (roles == null || roles.Count == 0)
                throw new ArgumentException("Roles cannot be null or empty.", nameof(roles));

            Console.WriteLine("Beginning UpsertGuildRoleChanges....");

            try
            {
                await using var conn = await OpenAsync();

                using (var transaction = await conn.BeginTransactionAsync())
                {
                    var insertSql = new StringBuilder(@"INSERT INTO role_map_profile (guild_id, role_id, role_name) VALUES ");
                    var insertParams = new List<NpgsqlParameter>();
                    var valueCount = 0;

                    var currentRoles = await conn.QueryAsync<(ulong, string)>(
                        "SELECT role_id, role_name FROM role_map_profile WHERE guild_id = @gid;",
                        new { gid = (long)guildId });

                    if (currentRoles.Any())
                    {
                        Console.WriteLine($"Found {currentRoles.Count()} existing roles for guild {guildId}. Checking for updates...");
                    }

                    foreach (var roleData in roles)
                    {
                        if (valueCount > 0) insertSql.Append(", ");

                        insertSql.Append($"(@gid, @rid{valueCount}, @rname{valueCount})");
                        insertParams.Add(new NpgsqlParameter($"rid{valueCount}", (long)roleData.Id));
                        insertParams.Add(new NpgsqlParameter($"rname{valueCount}", roleData.Name));
                        valueCount++;
                    }
                    insertSql.Append(" ON CONFLICT (guild_id, role_id) DO UPDATE SET role_name = EXCLUDED.role_name;");

                    using (var cmd = new NpgsqlCommand(insertSql.ToString(), conn, transaction))
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("gid", (long)guildId));
                        cmd.Parameters.AddRange(insertParams.ToArray());
                        var rowsAffected = await cmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"Upserted {rowsAffected} roles for guild {guildId}.");
                    }

                    await transaction.CommitAsync();
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return;
        }
    }
}

using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.Library.Services.Users
{
    public partial class UserService : IUserService
    {
        private readonly string _connectionString;
        public UserService(string connectionString, IntakeValidator validator)
        {
            _connectionString = connectionString;
        }
        public async Task<int> CreateUser(string username, string email, string password, int playerId = 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(username);
            ArgumentException.ThrowIfNullOrWhiteSpace(email);
            username = username.Trim();
            email = email.Trim().ToLowerInvariant();
            if (username.Length > 100 || email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email)
                throw new ArgumentException("Enter a valid username and email address.");
            var passwordHash = AccountPassword.Hash(password);
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            // Serialize registration for the same normalized email, including legacy mixed-case rows.
            await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext(@email))", new { email }, tx);
            if (await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE lower(email) = @email)", new { email }, tx))
                return 0;
            if (playerId == 0)
            {
                // NULL external IDs permit multiple unlinked local players under the existing unique constraint.
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    playerId = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000);
                    var inserted = await conn.ExecuteAsync(@"INSERT INTO players (id, player_name, startgg_link, user_link, last_updated)
                        VALUES (@playerId, @username, NULL, 0, CURRENT_TIMESTAMP) ON CONFLICT (id) DO NOTHING", new { playerId, username }, tx);
                    if (inserted == 1) break;
                    playerId = 0;
                }
                if (playerId == 0) throw new InvalidOperationException("Unable to allocate a local player ID.");
            }
            else
            {
                var existing = await conn.QuerySingleOrDefaultAsync<int?>("SELECT id FROM players WHERE id = @playerId FOR UPDATE", new { playerId }, tx);
                if (existing == null) throw new ArgumentException("The local player does not exist.");
                if (await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE player_id = @playerId)", new { playerId }, tx))
                    throw new InvalidOperationException("The player is already associated with an account.");
            }
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var userId = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000);
                var inserted = await conn.ExecuteAsync(@"INSERT INTO users (id, user_name, email, password, player_id, user_link)
                    VALUES (@userId, @username, @email, @passwordHash, @playerId, 0) ON CONFLICT (id) DO NOTHING",
                    new { userId, username, email, passwordHash, playerId }, tx);
                if (inserted == 0) continue;
                await tx.CommitAsync();
                return userId;
            }
            throw new InvalidOperationException("Unable to allocate a local user ID.");
        }
        public async Task<UserData?> GetUserById(int userId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                return await conn.QuerySingleOrDefaultAsync<UserData>(
                    @"SELECT id, user_name AS UserName, email, password,
                             player_id AS PlayerId, user_link AS UserLink
                      FROM users WHERE id = @userId", new { userId });
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Failed to retrieve the user from PostgreSQL.", ex);
            }
        }

        public async Task<bool> CheckUserById(int userId)
        {
            if (userId <= 0) return false;
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                return await conn.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS (SELECT 1 FROM users WHERE id = @userId)", new { userId });
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Failed to check the user in PostgreSQL.", ex);
            }
        }
    }
}

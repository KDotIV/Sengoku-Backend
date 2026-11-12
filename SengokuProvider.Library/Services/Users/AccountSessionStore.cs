using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using SengokuProvider.Library.Services.Users;
using Sengoku.Observability;

namespace SengokuProvider.Library.Services.Users;

public sealed record AccountIdentity(int UserId, int PlayerId, string UserName, bool IsAdmin = false);
public sealed record LoginRequest(string Email, string Password);
public sealed class AccountSessionStore(string connectionString)
{
    private static readonly string DummyPassword = AccountPassword.Hash("invalid-account-password-placeholder");
    public async Task<AccountIdentity?> Authenticate(string email, string password, CancellationToken ct)
    {
        return await OperationLog.RunAsync<AccountIdentity?>("AccountSessionStore.Authenticate", async () =>
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || string.IsNullOrEmpty(password) || password.Length > 1024) return null;
            await using var conn = new NpgsqlConnection(connectionString);

            var user = await conn.QuerySingleOrDefaultAsync<LoginRow>(new CommandDefinition(@"
            SELECT u.id AS UserId, u.player_id AS PlayerId, u.user_name AS UserName, u.password,
                EXISTS(SELECT 1 FROM user_permissions p WHERE p.id=u.id AND p.user_type=0) AS IsAdmin
            FROM users u WHERE lower(u.email) = @email", new { email = email.Trim().ToLowerInvariant() }, cancellationToken: ct));

            var valid = AccountPassword.Verify(password, user?.Password ?? DummyPassword);
            if (!valid || user == null) OperationLog.Current?.Reject("InvalidCredentials");
            else OperationLog.Identity(("UserId", user.UserId), ("Username", user.UserName), ("PlayerId", user.PlayerId));
            return valid && user != null ? new(user.UserId, user.PlayerId, user.UserName, user.IsAdmin) : null;
        });
    }
    public async Task<string> Create(int userId, string? previousToken, CancellationToken ct)
    {
        return await OperationLog.RunAsync<string>("AccountSessionStore.Create", async () =>
        {
            try
            {
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                await using var conn = new NpgsqlConnection(connectionString);

                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                if (ValidToken(previousToken))
                    await conn.ExecuteAsync(new CommandDefinition("DELETE FROM user_sessions WHERE token_hash = @hash", new { hash = Hash(previousToken!) }, tx, cancellationToken: ct));

                await conn.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO user_sessions (token_hash, user_id, expires_at)
                VALUES (@hash, @userId, CURRENT_TIMESTAMP + INTERVAL '8 hours')",
                    new { hash = Hash(token), userId }, tx, cancellationToken: ct));

                await tx.CommitAsync(ct);
                Sengoku.Observability.OperationLog.Event("Database.Committed", ("Committed", true));
                OperationLog.Identity(("UserId", userId), ("SessionCreated", true), ("SessionFingerprint", OperationDataPolicy.SessionFingerprint(token)));
                OperationLog.Event("Session.Committed", ("Committed", true));
                return token;
            }
            catch (NpgsqlException ex)
            {
                Sengoku.Observability.OperationLog.Failure(ex); throw new ApplicationException("Unable to create account session.", ex);
            }
        });
    }
    public async Task<AccountIdentity?> Find(string? token, CancellationToken ct)
    {
        return await OperationLog.RunAsync<AccountIdentity?>("AccountSessionStore.Find", async () =>
        {
            if (!ValidToken(token)) return null;
            await using var conn = new NpgsqlConnection(connectionString);

            return await conn.QuerySingleOrDefaultAsync<AccountIdentity>(new CommandDefinition(@"
            SELECT u.id AS UserId, u.player_id AS PlayerId, u.user_name AS UserName,
                EXISTS(SELECT 1 FROM user_permissions p WHERE p.id=u.id AND p.user_type=0) AS IsAdmin
            FROM user_sessions s JOIN users u ON u.id = s.user_id
            WHERE s.token_hash = @hash AND s.expires_at > CURRENT_TIMESTAMP", new { hash = Hash(token!) }, cancellationToken: ct));
        });
    }
    public async Task Revoke(string? token, CancellationToken ct)
    {
        await OperationLog.RunAsync("AccountSessionStore.Revoke", async () =>
        {
            if (!ValidToken(token)) return;
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);

                await conn.ExecuteAsync(new CommandDefinition("DELETE FROM user_sessions WHERE token_hash = @hash", new { hash = Hash(token!) }, cancellationToken: ct));
                OperationLog.Identity(("SessionRevoked", true));
            }
            catch (NpgsqlException ex)
            {
                Sengoku.Observability.OperationLog.Failure(ex); throw new ApplicationException("Unable to revoke account session.", ex);
            }
        });
    }
    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private sealed class LoginRow
    {
        public int UserId { get; set; }
        public int PlayerId { get; set; }
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
        public bool IsAdmin { get; set; }
    }
}


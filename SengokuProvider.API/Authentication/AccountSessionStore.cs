using Dapper;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using SengokuProvider.Library.Services.Users;

namespace SengokuProvider.API.Authentication;

public sealed record AccountIdentity(int UserId, int PlayerId, string UserName);
public sealed record LoginRequest(string Email, string Password);
public sealed class AccountSessionStore(string connectionString)
{
    private static readonly string DummyPassword = AccountPassword.Hash("invalid-account-password-placeholder");
    public async Task<AccountIdentity?> Authenticate(string email, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || string.IsNullOrEmpty(password) || password.Length > 1024) return null;
        await using var conn = new NpgsqlConnection(connectionString);

        var user = await conn.QuerySingleOrDefaultAsync<LoginRow>(new CommandDefinition(@"
            SELECT id AS UserId, player_id AS PlayerId, user_name AS UserName, password
            FROM users WHERE lower(email) = @email", new { email = email.Trim().ToLowerInvariant() }, cancellationToken: ct));

        var valid = AccountPassword.Verify(password, user?.Password ?? DummyPassword);
        return valid && user != null ? new(user.UserId, user.PlayerId, user.UserName) : null;
    }
    public async Task<string> Create(int userId, string? previousToken, CancellationToken ct)
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
        return token;
    }
    public async Task<AccountIdentity?> Find(string? token, CancellationToken ct)
    {
        if (!ValidToken(token)) return null;
        await using var conn = new NpgsqlConnection(connectionString);

        return await conn.QuerySingleOrDefaultAsync<AccountIdentity>(new CommandDefinition(@"
            SELECT u.id AS UserId, u.player_id AS PlayerId, u.user_name AS UserName
            FROM user_sessions s JOIN users u ON u.id = s.user_id
            WHERE s.token_hash = @hash AND s.expires_at > CURRENT_TIMESTAMP", new { hash = Hash(token!) }, cancellationToken: ct));
    }
    public async Task Revoke(string? token, CancellationToken ct)
    {
        if (!ValidToken(token)) return;

        await using var conn = new NpgsqlConnection(connectionString);

        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM user_sessions WHERE token_hash = @hash", new { hash = Hash(token!) }, cancellationToken: ct));
    }
    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private sealed class LoginRow
    {
        public int UserId { get; set; }
        public int PlayerId { get; set; }
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
    }
}

using Dapper;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.User;
using System.Data;

namespace SengokuProvider.Library.Services.Users;

public partial class UserService
{
    /// <summary>
    /// Persists a fetched public profile for an authorized local user/player pair.
    /// This association does not prove ownership of the external account.
    /// </summary>
    public async Task<StartggProfileLink> SaveStartggProfile(int userId, int playerId, CommonUserNode profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id <= 0 || profile.Player == null || profile.Player.Id <= 0 || string.IsNullOrWhiteSpace(profile.Slug))
            throw new ArgumentException("A complete start.gg user and player profile is required.", nameof(profile));
        var link = new StartggProfileLink(userId, playerId, profile.Id, profile.Player.Id, profile.Slug);
        var json = JsonConvert.SerializeObject(profile);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var transaction = await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            CommandDefinition Command(string sql, object? parameters = null) =>
                new(sql, parameters, transaction, cancellationToken: cancellationToken);

            var user = await conn.QuerySingleOrDefaultAsync<LocalUserLink>(Command(
                "SELECT player_id AS PlayerId, user_link AS UserLink FROM users WHERE id = @userId FOR UPDATE", new { userId }));
            if (user == null) throw new KeyNotFoundException("The local user does not exist.");
            var player = await conn.QuerySingleOrDefaultAsync<LocalPlayerLink>(Command(
                "SELECT startgg_link AS StartggLink, user_link AS UserLink FROM players WHERE id = @playerId FOR UPDATE", new { playerId }));
            if (player == null) throw new KeyNotFoundException("The local player does not exist.");
            if ((user.PlayerId > 0 && user.PlayerId != playerId) ||
                (user.UserLink > 0 && user.UserLink != link.StartggUserId) ||
                (player.StartggLink > 0 && player.StartggLink != link.StartggPlayerId) ||
                (player.UserLink > 0 && player.UserLink != link.StartggUserId))
                throw new InvalidOperationException("The local user or player is already linked to a different identity.");

            var parameters = new { userId, playerId, link.StartggUserId, link.StartggPlayerId, json };
            var conflict = await conn.ExecuteScalarAsync<bool>(Command(@"
                SELECT EXISTS (SELECT 1 FROM users WHERE id <> @userId
                    AND (user_link = @StartggUserId OR player_id = @playerId))
                OR EXISTS (SELECT 1 FROM players WHERE id <> @playerId
                    AND (startgg_link = @StartggPlayerId OR user_link = @StartggUserId))", parameters));
            if (conflict) throw new InvalidOperationException("The start.gg identity or local player is already linked elsewhere.");

            await conn.ExecuteAsync(Command(@"
                UPDATE players SET startgg_link = @StartggPlayerId, user_link = @StartggUserId,
                    last_updated = CURRENT_TIMESTAMP WHERE id = @playerId;
                UPDATE users SET player_id = @playerId, user_link = @StartggUserId,
                    startgg_profile = CAST(@json AS jsonb) WHERE id = @userId;", parameters));
            await transaction.CommitAsync(cancellationToken);
            return link;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("The start.gg identity or local player is already linked elsewhere.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure || ex.SqlState == PostgresErrorCodes.DeadlockDetected)
        {
            throw new InvalidOperationException("A concurrent link changed these identities. Retry the operation.", ex);
        }
        catch (NpgsqlException ex)
        {
            throw new ApplicationException("Failed to link the start.gg profile in PostgreSQL.", ex);
        }
        // Uncommitted transactions are rolled back by disposal, including on cancellation.
    }

    private sealed class LocalUserLink
    {
        public int? PlayerId { get; set; }
        public int? UserLink { get; set; }
    }

    private sealed class LocalPlayerLink
    {
        public int? StartggLink { get; set; }
        public int? UserLink { get; set; }
    }
}

using Dapper;
using Npgsql;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SengokuProvider.Library.Models.User;
namespace SengokuProvider.API.Authentication;

public sealed record VerifiedStartggLink(int UserId, int PlayerId, int StartggUserId, int StartggPlayerId, string Slug, DateTime VerifiedAt);

public sealed class StartggOAuthService(string connectionString, IConfiguration config, IHttpClientFactory clients)
{
    private const string Scope = "user.identity";
    public bool Enabled => !string.IsNullOrWhiteSpace(config["StartggOAuth:ClientId"]) && !string.IsNullOrWhiteSpace(config["StartggOAuth:ClientSecret"])
        && ValidHttps(config["StartggOAuth:RedirectUri"]);
    private static bool ValidHttps(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    private void CheckEnabled() { if (!Enabled) throw new InvalidOperationException("Start.gg verification is not configured."); }

    public async Task<string> Begin(int userId, string sessionToken, CancellationToken ct)
    {
        CheckEnabled();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var conn = new NpgsqlConnection(connectionString);

        var count = await conn.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO startgg_oauth_states(state_hash, session_hash, user_id, expires_at)
            SELECT @state, token_hash, user_id, CURRENT_TIMESTAMP + INTERVAL '10 minutes'
            FROM user_sessions WHERE token_hash = @session AND user_id = @userId AND expires_at > CURRENT_TIMESTAMP",
            new { state = Hash(state), session = Hash(sessionToken), userId }, cancellationToken: ct));

        if (count != 1) throw new UnauthorizedAccessException("Session expired.");
        return "https://start.gg/oauth/authorize?response_type=code&client_id=" + Uri.EscapeDataString(config["StartggOAuth:ClientId"]!) +
            "&scope=" + Scope + "&redirect_uri=" + Uri.EscapeDataString(config["StartggOAuth:RedirectUri"]!) + "&state=" + state;
    }
    public async Task<VerifiedStartggLink> Complete(int userId, string sessionToken, string? state, string? code, string? error, CancellationToken ct)
    {
        CheckEnabled();
        if (state is not { Length: 64 } || !state.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid OAuth state. Restart verification.");
        await using var conn = new NpgsqlConnection(connectionString);

        // Atomic consume: replay, cross-user/session and expired callbacks all fail before token exchange.
        var consumed = await conn.ExecuteAsync(new CommandDefinition(@"
            DELETE FROM startgg_oauth_states st USING user_sessions s
            WHERE st.state_hash = @state AND st.session_hash = @session AND st.user_id = @userId
                AND st.expires_at > CURRENT_TIMESTAMP AND s.token_hash = st.session_hash
                AND s.user_id = st.user_id AND s.expires_at > CURRENT_TIMESTAMP",
            new { state = Hash(state), session = Hash(sessionToken), userId }, cancellationToken: ct));

        if (consumed != 1) throw new ArgumentException("OAuth state expired or was already used. Restart verification.");
        if (!string.IsNullOrEmpty(error)) throw new ArgumentException("Start.gg verification was declined. Restart verification when ready.");
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096) throw new ArgumentException("Missing authorization code. Restart verification.");

        using var client = clients.CreateClient("StartggOAuth");
        using var tokenResponse = await client.PostAsJsonAsync("https://api.start.gg/oauth/access_token", new {
            grant_type = "authorization_code", client_id = config["StartggOAuth:ClientId"], client_secret = config["StartggOAuth:ClientSecret"],
            code, scope = Scope, redirect_uri = config["StartggOAuth:RedirectUri"]
        }, ct);

        if (!tokenResponse.IsSuccessStatusCode) throw new HttpRequestException("Start.gg rejected the authorization code.");
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));

        if (!tokenJson.RootElement.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(token.GetString()))
            throw new HttpRequestException("Start.gg returned no access token.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.start.gg/gql/alpha") {
            Content = JsonContent.Create(new { query = "query VerifyIdentity { currentUser { id name slug player { id gamerTag } } }" })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetString());

        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Start.gg identity lookup failed.");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        if (json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new HttpRequestException("Start.gg identity lookup returned errors.");

        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("currentUser", out var current) || current.ValueKind != JsonValueKind.Object)
            throw new HttpRequestException("Start.gg returned no current user.");

        var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonUserNode>(current.GetRawText());

        if (profile?.Id is not > 0 || profile.Player?.Id is not > 0 || string.IsNullOrWhiteSpace(profile.Slug))
            throw new HttpRequestException("Start.gg returned an incomplete identity.");

        // Provider tokens are deliberately not persisted: verification needs only this identity lookup.
        return await SaveVerifiedIdentity(userId, profile, ct);
    }
    internal async Task<VerifiedStartggLink> SaveVerifiedIdentity(int userId, CommonUserNode profile, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        CommandDefinition Cmd(string sql, object? values = null) => new(sql, values, tx, cancellationToken: ct);

        await conn.ExecuteAsync(Cmd("SELECT pg_advisory_xact_lock(@id)", new { id = (long)profile.Id }));
        var account = await conn.QuerySingleOrDefaultAsync<LinkAccount>(Cmd(@"SELECT player_id AS PlayerId, user_link AS UserLink,
            startgg_verified_user_id AS VerifiedUserId FROM users WHERE id = @userId FOR UPDATE", new { userId }));

        if (account == null) throw new UnauthorizedAccessException("Account no longer exists.");

        if ((account.VerifiedUserId > 0 && account.VerifiedUserId != profile.Id) || (account.UserLink > 0 && account.UserLink != profile.Id))
            throw new InvalidOperationException("This account is already linked to a different Start.gg user.");

        var local = await conn.QuerySingleOrDefaultAsync<LinkPlayer>(Cmd("SELECT id, startgg_link AS StartggLink, user_link AS UserLink FROM players WHERE id = @id FOR UPDATE", new { id = account.PlayerId }));

        if (local == null) throw new InvalidOperationException("The local player is missing.");
        if ((local.StartggLink > 0 && local.StartggLink != profile.Player.Id) || (local.UserLink > 0 && local.UserLink != profile.Id))
            throw new InvalidOperationException("The local player is already associated with another identity.");

        var imported = await conn.QuerySingleOrDefaultAsync<LinkPlayer>(Cmd("SELECT id, startgg_link AS StartggLink, user_link AS UserLink FROM players WHERE startgg_link = @id FOR UPDATE", new { id = profile.Player.Id }));

        if (imported?.UserLink > 0 && imported.UserLink != profile.Id) throw new InvalidOperationException("Imported player identity is inconsistent.");
        var playerId = imported?.Id ?? local.Id;

        var conflict = await conn.ExecuteScalarAsync<bool>(Cmd(@"SELECT EXISTS(SELECT 1 FROM users WHERE id <> @userId
            AND (user_link = @startggUserId OR startgg_verified_user_id = @startggUserId OR player_id = @playerId))",
            new { userId, startggUserId = profile.Id, playerId }));

        if (conflict) throw new InvalidOperationException("That Start.gg account or player is linked to another local account.");
        // Adopt an exact imported player match, preserving its standings/paths. Do not delete or merge the old placeholder.
        await conn.ExecuteAsync(Cmd(@"UPDATE players SET startgg_link = @startggPlayerId, user_link = @startggUserId, player_name = COALESCE(NULLIF(@gamerTag, ''), player_name), 
            last_updated = CURRENT_TIMESTAMP WHERE id = @playerId;
            UPDATE users SET player_id = @playerId, user_link = @startggUserId, startgg_profile = CAST(@profileJson AS jsonb), 
            startgg_verified_user_id = @startggUserId, startgg_verified_at = CURRENT_TIMESTAMP WHERE id = @userId",
            new { userId, playerId, gamerTag = profile.Player.GamerTag, startggPlayerId = profile.Player.Id, startggUserId = profile.Id,
                profileJson = Newtonsoft.Json.JsonConvert.SerializeObject(profile) }));

        var verifiedAt = await conn.ExecuteScalarAsync<DateTime>(Cmd("SELECT startgg_verified_at FROM users WHERE id = @userId", new { userId }));
        await tx.CommitAsync(ct);

        return new(userId, playerId, profile.Id, profile.Player.Id, profile.Slug!, verifiedAt);
    }
    public async Task<VerifiedStartggLink?> GetLink(int userId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        return await conn.QuerySingleOrDefaultAsync<VerifiedStartggLink>(new CommandDefinition(@"
            SELECT u.id AS UserId, u.player_id AS PlayerId, u.startgg_verified_user_id AS StartggUserId,
                p.startgg_link AS StartggPlayerId, u.startgg_profile->>'slug' AS Slug, u.startgg_verified_at AS VerifiedAt
            FROM users u JOIN players p ON p.id = u.player_id
            WHERE u.id = @userId AND u.startgg_verified_user_id = u.user_link AND p.user_link = u.user_link AND u.startgg_verified_at IS NOT NULL",
            new { userId }, cancellationToken: ct));
    }
    private sealed class LinkAccount
    { 
        public int PlayerId { get; set; } 
        public int UserLink { get; set; } 
        public int? VerifiedUserId { get; set; } 
    }
    private sealed class LinkPlayer 
    { 
        public int Id { get; set; } 
        public int? StartggLink { get; set; } 
        public int? UserLink { get; set; } 
    }
}

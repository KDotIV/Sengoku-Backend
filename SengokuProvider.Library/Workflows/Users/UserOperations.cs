using GraphQL;
using GraphQL.Client.Http;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using System.Text.RegularExpressions;

namespace SengokuProvider.Library.Workflows.Users;

/// <summary>Coordinates external lookups and user persistence without service-to-service cycles.</summary>
public sealed class UserOperations : IUserOperations
{
    private readonly IUserService _users;
    private readonly GraphQLHttpClient _client;
    private readonly RequestThrottler _throttler;
    private readonly IPlayerQueryService _players;

    public UserOperations(IUserService users, GraphQLHttpClient client, RequestThrottler throttler, IPlayerQueryService players)
    {
        _users = users;
        _client = client;
        _throttler = throttler;
        _players = players;
    }

    public Task<StartggProfileLink> LinkStartggProfileBySlug(int userId, int playerId, string urlSlug, CancellationToken cancellationToken = default) =>
        Link(userId, playerId, new GraphQLRequest
        {
            Query = "query LinkUserBySlug($slug: String!) { user(slug: $slug) { id name slug player { id gamerTag } } }",
            Variables = new { slug = NormalizeUserSlug(urlSlug) }
        }, cancellationToken);

    public Task<StartggProfileLink> LinkStartggProfileByUserId(int userId, int playerId, int startggUserId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startggUserId);
        return Link(userId, playerId, new GraphQLRequest
        {
            Query = "query LinkUserById($id: ID!) { user(id: $id) { id name slug player { id gamerTag } } }",
            Variables = new { id = startggUserId }
        }, cancellationToken);
    }

    private async Task<StartggProfileLink> Link(int userId, int playerId, GraphQLRequest request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        cancellationToken.ThrowIfCancellationRequested();
        await _throttler.WaitIfPaused();
        var response = await _client.SendQueryAsync<UserGraphQLResult>(request, cancellationToken);
        // GraphQL may return partial data alongside errors. Never persist partial identities.
        if (response.Errors is { Length: > 0 })
            throw new ApplicationException("start.gg rejected the profile lookup.");
        var profile = response.Data?.UserNode;
        if (profile == null || profile.Id <= 0)
            throw new KeyNotFoundException("The start.gg user was not found.");
        if (profile.Player == null || profile.Player.Id <= 0 || string.IsNullOrWhiteSpace(profile.Slug))
            throw new InvalidOperationException("The start.gg user has no complete player profile.");
        return await _users.SaveStartggProfile(userId, playerId, profile, cancellationToken);
    }

    public static string NormalizeUserSlug(string urlSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlSlug);
        var slug = urlSlug.Trim();
        if (slug.StartsWith("start.gg/", StringComparison.OrdinalIgnoreCase) || slug.StartsWith("www.start.gg/", StringComparison.OrdinalIgnoreCase))
            slug = "https://" + slug;
        if (Uri.TryCreate(slug, UriKind.Absolute, out var uri))
        {
            if ((uri.Scheme != "https" && uri.Scheme != "http") ||
                (uri.Host != "start.gg" && uri.Host != "www.start.gg") || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
                throw new ArgumentException("Expected a start.gg user profile URL.", nameof(urlSlug));
            slug = uri.AbsolutePath;
        }
        slug = slug.TrimStart('/');
        slug = slug.StartsWith("user/", StringComparison.Ordinal) ? slug.TrimEnd('/') : "user/" + slug;
        if (!Regex.IsMatch(slug, @"\Auser/[a-zA-Z0-9_-]+\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Expected a user/<slug> profile path or slug token.", nameof(urlSlug));
        return slug;
    }

    // Legacy lookup remains a preview; linking requires explicit local user and player identities.
    public async Task<UserPlayerDataResponse> SyncStartggDataToUserData(string playerName, string userSlug)
    {
        var data = !string.IsNullOrWhiteSpace(userSlug)
            ? await _players.GetUserDataByUserSlug(NormalizeUserSlug(userSlug))
            : await _players.GetUserDataByPlayerName(playerName);
        return new UserPlayerDataResponse
        {
            Data = data,
            Response = data.PlayerId > 0 ? "Successfully Retrieved User" : "Failed to Retrieve User"
        };
    }
}

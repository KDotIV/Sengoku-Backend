using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Library.Workflows.Events;
using SengokuProvider.Worker.Handlers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SengokuProvider.Library.Workflows.Legends;

public class LegendsOperations : ILegendsOperations
{
    private readonly string _connectionString;
    private readonly IConfiguration _config;
    private readonly ILegendIntakeService _legendIntake;
    private readonly ILegendQueryService _legendQuery;
    private readonly IEventOperations _eventIntake;
    private readonly IEventQueryService _eventQuery;
    private readonly IPlayerQueryService _playerQuery;
    private readonly IAzureBusApiService _bus;
    private readonly ICommonDatabaseService _common;
    private readonly IUserService _users;
    private static readonly Random Rand = new();

    public LegendsOperations(string connectionString, IConfiguration config, ILegendIntakeService legendIntake,
        ILegendQueryService legendQuery, IEventOperations eventIntake, IEventQueryService eventQuery,
        IPlayerQueryService playerQuery, IAzureBusApiService bus, ICommonDatabaseService common, IUserService users)
    {
        _connectionString = connectionString;
        _config = config;
        _legendIntake = legendIntake;
        _legendQuery = legendQuery;
        _eventIntake = eventIntake;
        _eventQuery = eventQuery;
        _playerQuery = playerQuery;
        _bus = bus;
        _common = common;
        _users = users;
    }

    public async Task<List<LegendData>> GenerateNewLegendsByPlayerStandings(List<PlayerStandingResult> standings)
    {
        //Need to refactor this to sort players standings into dictionary
        var legends = new List<LegendData>();
        if (standings == null || standings.Count == 0) return legends;

        
        var standingsDict = standings.GroupBy(s => s.TournamentLinks.PlayerId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var standing in standings)
        {
            if (standing.TournamentLinks == null || standing.StandingDetails == null) continue;
            try
            {
                var current = await _legendQuery.QueryStandingsByPlayerId(standing.TournamentLinks.PlayerId);
                if (current == null) throw new ArgumentNullException(nameof(current), "Player data cannot be null.");
                legends.Add(await _legendIntake.BuildLegendData(current, standing.StandingDetails.GamerTag ?? string.Empty));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Generating New Legend for PlayerID: {standing.TournamentLinks.PlayerId} - {ex.Message}");
                await SendPlayerIntakeMessage(standing.TournamentLinks.PlayerId, standing.StandingDetails.GamerTag ?? string.Empty);
            }
        }
        return legends;
    }

    public async Task<LeaderboardOnboardIntakeResult> IntakeTournamentStandingsByEventLink(int[] tournamentLinks, string eventLinkSlug, int[] gameIds, int leagueId, bool open = true)
    {
        var result = new LeaderboardOnboardIntakeResult
        {
            PlayerResult = new LeagueOnboardResult { Response = "" },
            TournamentResults = new TournamentOnboardResult { Response = "" }
        };
        if (tournamentLinks.Length == 0)
            tournamentLinks = (await _eventQuery.GetTournamentLinksByUrl(eventLinkSlug, gameIds)).Select(t => t.Id).ToArray();
        if (await UpdateTournamentStandings(tournamentLinks) == 0)
        {
            result.TournamentResults.Response = "Onboarding Failed. Check Logs";
            return result;
        }
        var playerIds = await ExtractPlayerIds(tournamentLinks);
        result.PlayerResult = await _legendIntake.AddPlayerToLeague(playerIds.ToArray(), leagueId);
        result.TournamentResults = await _legendIntake.AddTournamentToLeague(tournamentLinks, leagueId);
        return result;
    }

    public async Task<BoardRunnerResult> CreateNewRunnerBoard(List<int> tournamentIds, int userId, string userName, int orgId = default, string? orgName = default)
    {
        var result = new BoardRunnerResult { TournamentList = [], UserId = userId, OrgId = orgId, Response = "" };
        if (!await InsertNewRunnerBoard(tournamentIds, userId, userName, orgId, orgName)) return result;
        foreach (var tournament in await _eventQuery.GetTournamentLinksById(tournamentIds.ToArray()))
            result.TournamentList.Add(new TournamentBoardResult { TournamentId = tournament.Id, TournamentName = CleanUrlSlugName(tournament.UrlSlug), UrlSlug = tournament.UrlSlug, EntrantsNum = tournament.EntrantsNum, LastUpdated = tournament.LastUpdated });
        return result;
    }

    public async Task<string> AddUserToLeague(int playerId, string playerName, string playerEmail, int leagueId, int[] gameIds)
    {
        var added = await _legendIntake.AddPlayerToLeague([playerId], leagueId);
        if (added.Successful.Count == 0) return "This player is already registered...";
        try
        {
            return await _users.CreateUser(playerName, playerEmail, GenerateHashedPassword(), playerId) > 0
                ? "Successfully Registered!" : "User already registered";
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<bool> AddLeagueToUser(int leagueId, int userId)
    {
        if (leagueId < 0 || userId < 0) throw new ArgumentException("LeagueId and UserId must be valid");
        var user = await _users.GetUserById(userId);
        var leagues = await _legendQuery.GetLeagueByLeagueIds([leagueId]);
        if (leagues.Count == 0) throw new ArgumentNullException(nameof(leagues), "League results were empty");
        var league = leagues.First(x => x.LeagueId == leagueId);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"INSERT INTO user_leagues (user_id, user_name, league_id, league_name, last_updated)
            VALUES (@UserInput, @UserName, @LeagueInput, @LeagueName, @LastUpdated) ON CONFLICT DO NOTHING;", conn);
        cmd.Parameters.AddWithValue("@UserInput", userId);
        cmd.Parameters.AddWithValue("@UserName", user.UserName);
        cmd.Parameters.AddWithValue("@LeagueName", league.LeagueName);
        cmd.Parameters.AddWithValue("@LeagueInput", leagueId);
        cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private async Task<int> UpdateTournamentStandings(int[] ids)
    {
        try { await _eventQuery.GetTournamentLinksById(ids); return await _eventIntake.IntakeTournamentsByLinkId(ids); }
        catch (Exception ex) { Console.WriteLine($"Error while Updating Tournament Standings: {ex.Message} - {ex.StackTrace}"); return 0; }
    }

    private async Task<HashSet<int>> ExtractPlayerIds(int[] tournamentLinks)
    {
        var ids = new HashSet<int>();
        for (var i = 0; i < tournamentLinks.Length; i += 500)
            ids.UnionWith((await _playerQuery.GetRegisteredPlayersByTournamentId(tournamentLinks.Skip(i).Take(500).ToArray())).Select(p => p.Id));
        return ids;
    }

    private async Task<bool> SendPlayerIntakeMessage(int playerId, string gamerTag)
    {
        var queue = _config["ServiceBusSettings:PlayerReceivedQueue"];
        if (string.IsNullOrEmpty(queue) || string.IsNullOrEmpty(gamerTag) || playerId == 0) return false;
        var message = new PlayerReceivedData { Command = new OnboardPlayerDataCommand { Topic = CommandRegistry.OnboardPlayerData, PlayerId = playerId, GamerTag = gamerTag }, MessagePriority = MessagePriority.SystemIntake };
        return await _bus.SendBatchAsync(queue, JsonConvert.SerializeObject(message, JsonSettings.DefaultSettings));
    }

    private async Task<bool> InsertNewRunnerBoard(List<int> ids, int userId, string userName, int orgId, string? orgName)
    {
        if (userId < 0 || ids.Count == 0) return false;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"INSERT INTO bracket_boards (user_id, user_name, tournament_links, organization_id, organization_name, last_updated)
            VALUES (@UserInput, @UserName, @TournamentLinks, @OrgId, @OrgName, @LastUpdated) ON CONFLICT DO NOTHING RETURNING user_id;", conn);
        cmd.Parameters.AddWithValue("@UserInput", userId);
        cmd.Parameters.AddWithValue("@UserName", userName);
        cmd.Parameters.Add(_common.CreateDBIntArrayType("@TournamentLinks", ids.ToArray()));
        cmd.Parameters.AddWithValue("@OrgId", orgId);
        cmd.Parameters.AddWithValue("@OrgName", orgName ?? "");
        cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static string GenerateHashedPassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_@.";
        var regex = new Regex(@"^(?=.*[A-Za-z])(?=.*\d)(?=.*[@_.])[A-Za-z0-9@_.]{10}$");
        while (true) { var value = new string(Enumerable.Range(0, 10).Select(_ => chars[Rand.Next(chars.Length)]).ToArray()); if (regex.IsMatch(value)) return value; }
    }

    private static string CleanUrlSlugName(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return string.Empty;
        var tournament = Regex.Match(slug, @"tournament/([^/]+)/event");
        var gameEvent = Regex.Match(slug, @"event/([^/]+)");
        return $"{Clean(tournament.Success ? tournament.Groups[1].Value : "")} {Clean(gameEvent.Success ? gameEvent.Groups[1].Value : "")}".Trim();
    }

    private static string Clean(string value) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Regex.Replace(value, @"[^A-Za-z0-9#\s]", " ").ToLower());
}

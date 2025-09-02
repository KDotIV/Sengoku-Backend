using Dapper;
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
using SengokuProvider.Worker.Handlers;
using System.Collections.Concurrent;
using System.Text;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerIntakeService : IPlayerIntakeService
    {
        private readonly ICommonDatabaseService _commonDatabaseService;
        private readonly IPlayerQueryService _queryService;
        private readonly ILegendQueryService _legendQueryService;
        private readonly IEventQueryService _eventQueryService;
        private readonly IAzureBusApiService _azureBusApiService;
        private readonly IConfiguration _config;

        private readonly string _connectionString;
        private ConcurrentDictionary<int, int> _playersCache;
        private ConcurrentDictionary<int, string> _playerRegistry;
        private HashSet<int> _eventCache;
        private int _currentEventId;
        private static Random _rand = new Random();

        public PlayerIntakeService(string connectionString, IConfiguration configuration, ICommonDatabaseService commonServices, IPlayerQueryService playerQueryService,
            ILegendQueryService legendQueryService, IEventQueryService eventQueryService, IAzureBusApiService serviceBus)
        {
            _connectionString = connectionString;
            _config = configuration;
            _commonDatabaseService = commonServices;
            _queryService = playerQueryService;
            _legendQueryService = legendQueryService;
            _eventQueryService = eventQueryService;
            _azureBusApiService = serviceBus;
            _playersCache = new ConcurrentDictionary<int, int>();
            _playerRegistry = new ConcurrentDictionary<int, string>();
            _eventCache = new HashSet<int>();
        }
        public async Task<bool> SendPlayerIntakeMessage(int tournamentLink)
        {
            if (_config == null || string.IsNullOrEmpty(_config["ServiceBusSettings:PlayerReceivedQueue"]))
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            if (tournamentLink == 0)
            {
                Console.WriteLine("Event Url cannot be null or empty");
                return false;
            }

            try
            {
                var newCommand = new PlayerReceivedData
                {
                    Command = new IntakePlayersByTournamentCommand
                    {
                        Topic = CommandRegistry.IntakePlayersByTournament,
                        TournamentLink = tournamentLink,
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:PlayerReceivedQueue"], messageJson);
                if (!result)
                {
                    Console.WriteLine("Failed to Send Service Bus Message to Event Received Queue. Check Data");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        public async Task<int> IntakePlayerData(int tournamentLink)
        {
            try
            {
                PlayerGraphQLResult? newPlayerData = await _queryService.QueryPlayerDataFromStartgg(tournamentLink);
                if (newPlayerData == null) { return 0; }

                _eventCache.Add(newPlayerData.TournamentLink.EventLink.Id);
                _currentEventId = newPlayerData.TournamentLink.EventLink.Id;
                int playerSuccess = await ProcessPlayerData(newPlayerData);

                Console.WriteLine($"Players Inserted from Registry: {_playerRegistry.Count}");

                Console.WriteLine("Starting Standings Processing");
                var standingsSuccess = await ProcessNewPlayerStandings(newPlayerData);

                Console.WriteLine($"{standingsSuccess} total standings added for player");

                return standingsSuccess;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred during Player Intake: {ex.StackTrace}", ex);
            }
        }
        public async Task<int> OnboardPreviousTournamentData(OnboardPlayerDataCommand command, int volumeLimit = 100)
        {
            List<Task<int>> batchTasks = new List<Task<int>>();
            List<PlayerStandingResult> currentBatch = new List<PlayerStandingResult>();
            try
            {
                PastEventPlayerData queryResult = await _queryService.QueryStartggPreviousEventData(command.PlayerId, command.GamerTag, command.PerPage);

                if (queryResult == null || queryResult.PlayerQuery == null || queryResult.PlayerQuery.User == null || queryResult.PlayerQuery.User.Events == null || queryResult?.PlayerQuery?.User?.Events?.Nodes?.Count == 0) { return 0; }

                var mappedResult = MapPreviousTournamentData(queryResult);
                var standingsSuccess = await IntakePlayerStandingData(mappedResult);

                Console.WriteLine($"{standingsSuccess} total standings added for player");

                return standingsSuccess;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred during Player Intake: {ex.StackTrace}", ex);
            }
        }
        public async Task<PlayerOnboardResult> OnboardBracketRunnerByBracketSlug(string bracketSlug, int playerId)
        {
            var onboardResult = new PlayerOnboardResult { Response = "Open" };

            if (string.IsNullOrEmpty(bracketSlug) || playerId <= 0)
            {
                onboardResult.Response = "FAILED: BracketSlug or PlayerId cannot be null or empty";
                return onboardResult;
            }
            (bool flowControl, PlayerOnboardResult value, string[] returnedSlug) = await VerifyBracketSlug(bracketSlug, onboardResult);
            if (!flowControl)
            {
                return value;
            }

            int tempBracketId = Convert.ToInt32(returnedSlug[2]);
            int tempGroupPhaseId = Convert.ToInt32(returnedSlug[1]);
            int tempTournamentId = Convert.ToInt32(returnedSlug[3]);
            var bracketData = await _queryService.QueryBracketDataFromStartggByBracketId(tempBracketId);
            BracketVictoryPathData processedData = await ProcessNewBracketData(bracketData, playerId, tempTournamentId);

            onboardResult = await SaveVictoryPathData(processedData);

            return onboardResult;
        }
        private async Task<PlayerOnboardResult> SaveVictoryPathData(BracketVictoryPathData processedData)
        {
            if (processedData == null || processedData.EntrantSetCards == null || processedData.EntrantSetCards.Count == 0)
            {
                return new PlayerOnboardResult { Response = "FAILED: Cannot save invalid Bracket Data" };
            }

            var setResponse = await SaveTournamentSetData(processedData.EntrantSetCards);
            if (setResponse.Successful.Count == 0) { setResponse.Response = "No Sets were inserted. Can't Create Victory Path"; return setResponse; }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var setIds = _commonDatabaseService.CreateDBIntArrayType("@SetIds", setResponse.Successful.ToArray());
                var newPathId = await GenerateNewBracketPathId();
                try
                {
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO bracket_paths (id, tournament_link, tournament_name, event_link, round_num, player_id, last_updated, set_ids) VALUES (@ID, @TournamentLink, @TournamentName, @EventLink, @RoundNum, @PlayerId, @LastUpdated, @SetIds) ON CONFLICT DO NOTHING;", conn))
                    {
                        cmd.Parameters.AddWithValue("@ID", newPathId);
                        cmd.Parameters.AddWithValue("@TournamentLink", processedData.TournamentLinkID);
                        cmd.Parameters.AddWithValue("@TournamentName", processedData.TournamentName);
                        cmd.Parameters.AddWithValue("@EventLink", processedData.EventLinkID);
                        cmd.Parameters.AddWithValue("@RoundNum", processedData.RoundNum);
                        cmd.Parameters.AddWithValue("@PlayerId", processedData.PlayerTournamentCard.PlayerID);
                        cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);
                        cmd.Parameters.Add(setIds);

                        var result = await cmd.ExecuteNonQueryAsync();

                        if (result > 0)
                        {
                            Console.WriteLine($"Victory Path Inserted for Player: {processedData.PlayerTournamentCard.PlayerID} in Tournament: {processedData.TournamentLinkID}");
                            setResponse.Response = $"Bracket Path Inserted Successfully: PlayerID: {processedData.PlayerTournamentCard.PlayerID} Tournament: {processedData.TournamentLinkID}";
                            return setResponse;
                        }
                        else
                        {
                            Console.WriteLine($"Victory Path already exists for Player: {processedData.PlayerTournamentCard.PlayerID} in Tournament: {processedData.TournamentLinkID}");
                            setResponse.Response = "Victory Path already exists";
                            return setResponse;
                        }
                    }
                }
                catch (NpgsqlException ex)
                {
                    throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"Error While Processing: {ex.Message} - {ex.StackTrace}");
                }
            }
        }
        private async Task<PlayerOnboardResult> SaveTournamentSetData(List<EntrantSetCard> entrantsData)
        {
            var onboardResult = new PlayerOnboardResult { Response = "Open" };
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var entrantCard in entrantsData)
                    {
                        if (entrantCard.PlayerOneID == 0 || entrantCard.PlayerTwoID == 0)
                        {
                            Console.WriteLine("Entrant Card is missing Player IDs. Cannot insert into database");
                            onboardResult.Failures.Add(entrantCard.SetID);
                            continue;
                        }
                        try
                        {
                            using (var cmd = new NpgsqlCommand(@"INSERT INTO tournament_sets (id, playerone_id, playerone_name, playertwo_id, playertwo_name, last_updated) VALUES (@SetId, @PlayerOneId, @PlayerOneName, @PlayerTwoId, @PlayerTwoName, @LastUpdated) ON CONFLICT DO NOTHING;", conn))
                            {
                                cmd.Parameters.AddWithValue("@SetId", entrantCard.SetID);
                                cmd.Parameters.AddWithValue("@PlayerOneId", entrantCard.PlayerOneID);
                                cmd.Parameters.AddWithValue("@PlayerOneName", entrantCard.EntrantOneName);
                                cmd.Parameters.AddWithValue("@PlayerTwoId", entrantCard.PlayerTwoID);
                                cmd.Parameters.AddWithValue("@PlayerTwoName", entrantCard.EntrantTwoName);
                                cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);

                                var result = await cmd.ExecuteNonQueryAsync();
                                if (result == 0)
                                {
                                    Console.WriteLine($"Set already exists in database for Players: {entrantCard.PlayerOneID} vs {entrantCard.PlayerTwoID}");
                                    onboardResult.Successful.Add(entrantCard.SetID);
                                }
                                if (result > 0)
                                {
                                    Console.WriteLine($"Set Inserted for Players: {entrantCard.PlayerOneID} vs {entrantCard.PlayerTwoID}");
                                    onboardResult.Successful.Add(entrantCard.SetID);
                                }
                            }
                        }
                        catch (NpgsqlException ex)
                        {
                            onboardResult.Response = ex.Message;
                            onboardResult.Failures.Add(entrantCard.SetID);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            onboardResult.Response = ex.Message;
                            onboardResult.Failures.Add(entrantCard.SetID);
                            continue;
                        }
                    }
                    await transaction.CommitAsync();
                }
                await conn.CloseAsync();
            }
            return onboardResult;
        }
        private async Task<BracketVictoryPathData> ProcessNewBracketData(PhaseGroupGraphQL bracketData, int playerId, int tournamentId)
        {
            if (bracketData == null || bracketData.PhaseGroup == null || bracketData.PhaseGroup.Sets.Nodes == null || bracketData.PhaseGroup.Id == 0 || bracketData.PhaseGroup.Sets.Nodes.Count == 0)
            {
                throw new ApplicationException("No Data to process");
            }
            try
            {
                var result = new BracketVictoryPathData
                {
                    TournamentLinkID = tournamentId,
                    EventLinkID = 0,
                    TournamentName = "Unknown",
                    RoundNum = bracketData.PhaseGroup.DisplayIdentifier ?? "Unknown",
                    PlayerTournamentCard = new PlayerTournamentCard
                    {
                        PlayerID = playerId,
                        PlayerName = "Unknown",
                        PlayerResults = new List<PlayerStandingResult>()
                    },
                    EntrantSetCards = new List<EntrantSetCard>()
                };
                var tempPlayerArr = new int[] { playerId };
                var tempTournamentArr = new int[] { tournamentId };
                List<PlayerStandingResult> playerStanding = await _queryService.GetStandingsDataByPlayerIds(tempPlayerArr, tempTournamentArr);
                if (playerStanding == null || playerStanding.Count == 0)
                {
                    throw new ApplicationException("No Player Standing Data to process");
                }
                PlayerStandingResult? firstRecord = playerStanding.FirstOrDefault(x => x.TournamentLinks?.PlayerId == playerId);
                if (firstRecord == null || firstRecord.TournamentLinks == null || firstRecord.TournamentLinks.EntrantId == 0)
                {
                    throw new ApplicationException("No Player Standing Data found for the provided PlayerId");
                }

                result.PlayerTournamentCard.PlayerName = firstRecord.StandingDetails.GamerTag ?? "Unknown";
                result.TournamentLinkID = firstRecord.StandingDetails.TournamentId;
                result.EventLinkID = firstRecord.StandingDetails.EventId;
                result.TournamentName = firstRecord.StandingDetails.TournamentName ?? "Unknown";

                List<EntrantSetCard> entrantSetCards = await ReduceBracketDataForEntrantsCards(bracketData.PhaseGroup.Sets.Nodes, playerId,
                    firstRecord.TournamentLinks.EntrantId);

                if (entrantSetCards == null || entrantSetCards.Count == 0) { throw new ApplicationException("Unable to Reduce Bracket data from Dataset with provided PlayerId"); }

                result.EntrantSetCards = entrantSetCards;

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine("BracketRunner error occurred: ", ex);
                throw;
            }
        }
        private async Task<List<EntrantSetCard>> ReduceBracketDataForEntrantsCards(List<SetNode> nodes, int playerId, int entrantId)
        {
            List<EntrantSetCard> entrantSetCards = new List<EntrantSetCard>();
            ConcurrentDictionary<int, int> entrantsRegistry = new ConcurrentDictionary<int, int>();

            foreach (var set in nodes)
            {
                if (set.Slots == null || set.Slots.Count < 2) continue;
                var entrantOne = set.Slots[0].Entrant;
                var entrantTwo = set.Slots[1].Entrant;
                if (entrantOne == null || entrantTwo == null) continue;
                if (entrantOne.Id == entrantId || entrantTwo.Id == entrantId)
                {
                    if (entrantOne.Id == entrantTwo.Id)
                    {
                        Console.WriteLine($"Entrant One and Two are the same: {entrantOne.Name} - {entrantOne.Id}");
                        continue;
                    }
                    EntrantSetCard newCard = new EntrantSetCard
                    {
                        EntrantOneID = entrantOne.Id,
                        EntrantOneName = entrantOne.Name ?? "Unknown",
                        EntrantTwoID = entrantTwo.Id,
                        EntrantTwoName = entrantTwo.Name ?? "Unknown",
                        SetID = set.Id
                    };
                    entrantSetCards.Add(newCard);
                    entrantsRegistry.TryAdd(entrantOne.Id != entrantId ? entrantOne.Id : entrantTwo.Id, 0);
                }
            }
            if (entrantSetCards.Count == 0)
            {
                Console.WriteLine("No Entrant Set Cards found for the provided PlayerId");
                return entrantSetCards;
            }
            List<Links> playerIds = await _queryService.GetPlayersByEntrantLinks(entrantsRegistry.Keys.ToArray());
            if (playerIds.Count == 0) throw new ApplicationException("No Player IDs found for the provided Entrant IDs");

            foreach (var entrantCard in entrantSetCards)
            {
                Links? currentLink = entrantCard.EntrantOneID != entrantId
                    ? playerIds.FirstOrDefault(x => x.EntrantId == entrantCard.EntrantOneID)
                    : playerIds.FirstOrDefault(x => x.EntrantId == entrantCard.EntrantTwoID);

                if (currentLink == null || currentLink.EntrantId == 0) continue;

                if (currentLink.EntrantId == entrantCard.EntrantOneID)
                {
                    entrantCard.PlayerOneID = currentLink.PlayerId;
                    entrantCard.PlayerTwoID = playerId;
                }
                else
                {
                    entrantCard.PlayerTwoID = currentLink.PlayerId;
                    entrantCard.PlayerOneID = playerId;
                }
            }
            // Remove duplicates based on Entrant IDs
            entrantSetCards = entrantSetCards
                .GroupBy(x => new { x.EntrantOneID, x.EntrantTwoID })
                .Select(g => g.First())
                .ToList();
            return entrantSetCards;
        }
        private async Task<(bool flowControl, PlayerOnboardResult value, string[] returnedSlug)> VerifyBracketSlug(string bracketSlug, PlayerOnboardResult onboardResult)
        {
            if (!Uri.TryCreate(bracketSlug, UriKind.Absolute, out var uri))
            {
                onboardResult.Response = "FAILED: bracketSlug is not a valid URL";
                return (false, onboardResult, Array.Empty<string>());
            }
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Find "brackets" marker
            var bracketIndex = Array.IndexOf(segments, "brackets");
            // we need at least two IDs after it
            if (bracketIndex < 0 || segments.Length < bracketIndex + 3)
            {
                onboardResult.Response = "FAILED: URL must contain '/brackets/{id1}/{id2}'";
                return (false, onboardResult, Array.Empty<string>());
            }

            var firstPart = string.Join("/", segments.Take(bracketIndex));
            var id1 = segments[bracketIndex + 1];
            var id2 = segments[bracketIndex + 2];

            TournamentData tournamentLink = await _eventQueryService.GetTournamentLinkbyUrl(firstPart);
            if (tournamentLink == null || tournamentLink.Id == 0)
            {
                onboardResult.Response = "FAILED: Tournament Link not found for the provided URL";
                return (false, onboardResult, Array.Empty<string>());
            }
            return (flowControl: true, value: onboardResult, returnedSlug: new[] { firstPart, id1, id2, tournamentLink.Id.ToString() });
        }
        private List<PlayerStandingResult> MapPreviousTournamentData(PastEventPlayerData? playerData)
        {
            List<PlayerStandingResult> mappedResult = new List<PlayerStandingResult>();

            if (playerData == null || playerData.PlayerQuery?.User?.Events?.Nodes == null || playerData.PlayerQuery.User.Events.Nodes.Count == 0)
            {
                Console.WriteLine("No PastPlayerData to process");
                return mappedResult;
            }

            foreach (var tempNode in playerData.PlayerQuery.User.Events.Nodes)
            {
                if (tempNode == null || tempNode.Entrants?.Nodes == null || tempNode.Entrants.Nodes.Count == 0 || tempNode.NumEntrants == 0)
                {
                    continue;
                }

                var firstRecord = tempNode.Entrants.Nodes.First();
                if (firstRecord.Standing == null)
                {
                    continue;
                }

                int numEntrants = tempNode.NumEntrants ?? 0;
                int totalPoints = CalculateLeaguePoints(firstRecord, numEntrants);

                var newStanding = new PlayerStandingResult
                {
                    Response = "Open",
                    EntrantsNum = numEntrants,
                    UrlSlug = tempNode.Slug ?? string.Empty,
                    LastUpdated = DateTime.UtcNow,
                    StandingDetails = new StandingDetails
                    {
                        IsActive = firstRecord.Standing.IsActive ?? false,
                        Placement = firstRecord.Standing.Placement ?? 0,
                        GamerTag = playerData.PlayerQuery.GamerTag ?? string.Empty,
                        EventId = tempNode.EventLink?.Id ?? 0,
                        EventName = tempNode.EventLink?.Name ?? string.Empty,
                        TournamentId = tempNode.Id,
                        TournamentName = tempNode.Name ?? string.Empty
                    },
                    TournamentLinks = new Links
                    {
                        EntrantId = firstRecord.Id,
                        StandingId = firstRecord.Standing.Id,
                        PlayerId = firstRecord?.Participants?.FirstOrDefault()?.Player?.Id ?? 0
                    }
                };
                mappedResult.Add(newStanding);
            }
            return mappedResult;
        }
        private async Task<int> ProcessNewPlayerStandings(PlayerGraphQLResult tournamentData, int volumeLimit = 100)
        {

            var mappedStandings = MapStandingsData(tournamentData);
            var result = await IntakePlayerStandingData(mappedStandings);
            return result;
        }
        private List<PlayerStandingResult> MapStandingsData(PlayerGraphQLResult? data)
        {
            List<PlayerStandingResult> mappedResult = new List<PlayerStandingResult>();
            if (data == null) return mappedResult;

            var allIds = data.TournamentLink.Entrants.Nodes
                           .Select(n => n.Id);

            var duplicates = allIds
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => new { EntrantId = g.Key, Count = g.Count() })
                .ToList();

            if (duplicates.Any())
            {
                Console.WriteLine("Found duplicate entrant IDs:");
                foreach (var dup in duplicates)
                    Console.WriteLine($"  • {dup.EntrantId} appears {dup.Count} times");
            }
            else
            {
                Console.WriteLine("No duplicate entrant IDs detected.");
            }

            Dictionary<int, int> entrantsRegistry = new Dictionary<int, int>();
            foreach (var tempNode in data.TournamentLink.Entrants.Nodes)
            {
                if (tempNode.Standing == null) continue;
                int numEntrants = data.TournamentLink.NumEntrants ?? 0;
                try
                {
                    int totalPoints = CalculateLeaguePoints(tempNode, numEntrants);
                    var newStandings = new PlayerStandingResult
                    {
                        Response = "Open",
                        EntrantsNum = numEntrants,
                        LastUpdated = DateTime.UtcNow,
                        UrlSlug = data.TournamentLink.Slug,
                        StandingDetails = new StandingDetails
                        {
                            IsActive = tempNode.Standing.IsActive ?? false,
                            Placement = tempNode.Standing.Placement ?? 0,
                            GamerTag = tempNode.Participants?.FirstOrDefault()?.Player?.GamerTag ?? "",
                            EventId = data.TournamentLink.EventLink.Id,
                            EventName = data.TournamentLink.EventLink.Name,
                            TournamentId = data.TournamentLink.Id,
                            TournamentName = data.TournamentLink.Name,
                            LeaguePoints = totalPoints
                        },
                        TournamentLinks = new Links
                        {
                            EntrantId = tempNode.Id,
                            StandingId = tempNode.Standing.Id,
                            PlayerId = tempNode.Participants?.FirstOrDefault()?.Player?.Id ?? 0,
                        }
                    };
                    mappedResult.Add(newStandings);
                    entrantsRegistry[tempNode.Id] = mappedResult.Count - 1; // Track index in list
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error Occured populating Standing Data: {ex.Message}, {ex.StackTrace}");
                    continue;
                }
            }
            return mappedResult;
        }
        private int CalculateLeaguePoints(CommonEntrantNode tempNode, int totalEntrants, bool isRookieEvent = false)
        {
            // Base participation points
            int participationPoints = 10;
            double multiplier = 1.0;

            if (isRookieEvent)
            {
                participationPoints = 5;
                multiplier = CommonConstants.RookieMultiplier;
            }

            double totalPoints = participationPoints;

            int placement = tempNode.Standing?.Placement ?? int.MaxValue;

            // Apply main or rookie distribution
            foreach (var entry in CommonConstants.EnhancedPointDistribution)
            {
                if (placement <= entry.Key)
                {
                    totalPoints += entry.Value;
                    break;
                }
            }

            totalPoints *= multiplier;

            // Use a logarithmic scale to reduce the impact of large tournaments
            double entrantFactor = Math.Log(totalEntrants + 1); // +1 to avoid log(1) = 0
            totalPoints *= entrantFactor;

            // Ensure minimum points for participation
            int finalPoints = (int)Math.Floor(totalPoints);
            if (finalPoints < participationPoints)
            {
                finalPoints = participationPoints;
            }

            return finalPoints;
        }
        private async Task<int> IntakePlayerStandingData(List<PlayerStandingResult> currentStandings)
        {
            if (currentStandings == null || currentStandings.Count == 0) return 0;

            int totalSuccess = 0;
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        var insertQuery = new StringBuilder(@"INSERT INTO standings (entrant_id, player_id, tournament_link, placement, entrants_num, active, gained_points, last_updated) VALUES ");
                        var queryParams = new List<NpgsqlParameter>();
                        var valueCount = 0;

                        var uniqueStandings = currentStandings
                            .GroupBy(s => s.TournamentLinks!.EntrantId)
                            .Select(g => g.OrderBy(x => x.StandingDetails.Placement)
                            .First())
                            .ToList();

                        foreach (var data in uniqueStandings)
                        {
                            var tempArrInput = new int[] { data.StandingDetails.TournamentId };
                            var checkTournamentLink = await _eventQueryService.GetTournamentLinksById(tempArrInput);
                            if (checkTournamentLink.Count == 0 || checkTournamentLink.FirstOrDefault()?.Id == 0)
                            {
                                Console.WriteLine($"TournamentLink does not exist for this Standing Data. Sending TournamentLink: {data.StandingDetails.TournamentId} to EventQueue");
                                await SendTournamentLinkEventMessage(data.StandingDetails.EventId);
                                continue;
                            }

                            if (data.TournamentLinks == null || data.TournamentLinks.PlayerId == 0)
                            {
                                Console.WriteLine("Standing Data is missing Player Startgg link. Can't link to player");
                                continue;
                            }

                            int exists = await VerifyPlayer(data.TournamentLinks.PlayerId);
                            if (exists == 0)
                            {
                                Console.WriteLine("Player does not exist. Sending request to intake player");
                                continue;
                            }
                            if (valueCount > 0)
                            {
                                insertQuery.Append(", ");
                            }
                            insertQuery.Append($"(@EntrantInput{valueCount}, @PlayerId{valueCount}, @TournamentLink{valueCount}, @PlacementInput{valueCount}, @NumEntrants{valueCount}, @IsActive{valueCount}, @NewPoints{valueCount}, @LastUpdated{valueCount})");

                            queryParams.Add(new NpgsqlParameter($"@EntrantInput{valueCount}", data.TournamentLinks.EntrantId));
                            queryParams.Add(new NpgsqlParameter($"@PlayerId{valueCount}", exists));
                            queryParams.Add(new NpgsqlParameter($"@TournamentLink{valueCount}", data.StandingDetails.TournamentId));
                            queryParams.Add(new NpgsqlParameter($"@PlacementInput{valueCount}", data.StandingDetails.Placement));
                            queryParams.Add(new NpgsqlParameter($"@NumEntrants{valueCount}", data.EntrantsNum));
                            queryParams.Add(new NpgsqlParameter($"@IsActive{valueCount}", data.StandingDetails.IsActive));
                            queryParams.Add(new NpgsqlParameter($"@NewPoints{valueCount}", data.StandingDetails.LeaguePoints));
                            queryParams.Add(new NpgsqlParameter($"@LastUpdated{valueCount}", data.LastUpdated));

                            valueCount++;
                        }
                        insertQuery.Append(" ON CONFLICT (entrant_id) DO UPDATE SET player_id = EXCLUDED.player_id, tournament_link = EXCLUDED.tournament_link, placement = EXCLUDED.placement, entrants_num = EXCLUDED.entrants_num, active = EXCLUDED.active, gained_points = EXCLUDED.gained_points, last_updated = EXCLUDED.last_updated;");

                        using (var cmd = new NpgsqlCommand(insertQuery.ToString(), conn))
                        {
                            cmd.Transaction = transaction;
                            cmd.Parameters.AddRange(queryParams.ToArray());
                            var result = await cmd.ExecuteNonQueryAsync();
                            if (result > 0)
                            {
                                totalSuccess = result;
                                Console.WriteLine($"Current Success: {result}");
                            }
                        }

                        await transaction.CommitAsync();
                    }
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

            return totalSuccess;
        }
        private async Task SendOnboardMessage(int playerId, string playerName)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:legendreceivedqueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return;
            }
            try
            {
                var newCommand = new OnboardReceivedData
                {
                    Command = new OnboardPlayerDataCommand
                    {
                        PlayerId = playerId,
                        GamerTag = playerName,
                        Topic = CommandRegistry.OnboardPlayerData,
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:legendreceivedqueue"], messageJson);

                if (!result)
                {
                    Console.WriteLine("Failed to Send Onboarding Message to Service Bus. Check Data");
                    return;
                }
                _playerRegistry.TryRemove(playerId, out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected Error Sending Onboarding Message {ex.Message} {ex.StackTrace}");
            }
        }
        private async Task<bool> SendTournamentLinkEventMessage(int eventLinkId)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:eventreceivedqueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            try
            {
                var newCommand = new EventReceivedData
                {
                    Command = new LinkTournamentByEventIdCommand
                    {
                        EventLinkId = eventLinkId,
                        Topic = CommandRegistry.LinkTournamentByEvent,
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:eventreceivedqueue"], messageJson);

                if (!result)
                {
                    Console.WriteLine("Failed to Send Service Bus Message to Event Received Queue. Check Data");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<int> VerifyPlayer(int playerId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand(@"SELECT id FROM players WHERE startgg_link = @Input", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", playerId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                return reader.GetInt32(reader.GetOrdinal("id"));
                            }
                        }
                    }
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
            return 0;
        }
        private async Task<int> ProcessPlayerData(PlayerGraphQLResult queryData)
        {
            var players = new List<PlayerData>();
            if (queryData.TournamentLink == null) throw new ApplicationException("Player Query Data was null from Start.gg");
            foreach (var node in queryData.TournamentLink.Entrants.Nodes)
            {
                var firstRecord = node.Participants.FirstOrDefault();
                if (firstRecord == null) continue;
                if (!_playersCache.TryGetValue(firstRecord.Player.Id, out int databaseId))
                {
                    if (firstRecord.User == null) continue;
                    databaseId = await CheckDuplicatePlayer(firstRecord);
                    if (databaseId == 0)
                    {
                        var newPlayerData = new PlayerData
                        {
                            Id = await GenerateNewPlayerId(),
                            PlayerName = firstRecord.Player.GamerTag,
                            PlayerLinkID = firstRecord.Player.Id,
                            LastUpdate = DateTime.UtcNow,
                            UserLink = firstRecord.User.Id
                        };
                        players.Add(newPlayerData);
                        databaseId = newPlayerData.Id;
                    }
                }
                _playerRegistry.TryAdd(databaseId, firstRecord.Player.GamerTag);
            }
            return await InsertNewPlayerData(players);
        }
        private async Task<int> InsertNewPlayerData(List<PlayerData> players)
        {
            try
            {
                int totalSuccess = 0;
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        foreach (var player in players)
                        {
                            var createInsertCommand = @"
                            INSERT INTO players (id, player_name, startgg_link, last_updated, user_link)
                            VALUES (@IdInput, @PlayerName, @PlayerLinkId, @LastUpdated, @UserLink)
                            ON CONFLICT (startgg_link) DO UPDATE SET
                                player_name = EXCLUDED.player_name,
                                startgg_link = EXCLUDED.startgg_link,
                                last_updated = EXCLUDED.last_updated,
                                user_link = EXCLUDED.user_link;";
                            using (var cmd = new NpgsqlCommand(createInsertCommand, conn))
                            {
                                cmd.Transaction = transaction;
                                cmd.Parameters.AddWithValue("@IdInput", player.Id);
                                cmd.Parameters.AddWithValue("@PlayerName", player.PlayerName);
                                cmd.Parameters.AddWithValue("@PlayerLinkId", player.PlayerLinkID);
                                cmd.Parameters.AddWithValue("@LastUpdated", player.LastUpdate);
                                cmd.Parameters.AddWithValue("@UserLink", player.UserLink);
                                int result = await cmd.ExecuteNonQueryAsync();
                                if (result > 0) { Console.WriteLine("Player Inserted"); totalSuccess += result; }
                            }
                        }
                        await transaction.CommitAsync();
                    }
                }
                return totalSuccess;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return 0;
        }
        private async Task<int> GenerateNewPlayerId()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                while (true)
                {
                    var newId = _rand.Next(100000, 1000000);

                    var newQuery = @"SELECT id FROM players where id = @Input";
                    var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                    if (newId != queryResult || queryResult == 0) return newId;
                }
            }
        }
        private async Task<int> GenerateNewBracketPathId()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                while (true)
                {
                    var newId = _rand.Next(100000, 1000000);
                    var newQuery = @"SELECT id FROM bracket_paths where id = @Input";
                    var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                    if (newId != queryResult || queryResult == 0) return newId;
                }
            }
        }
        private async Task<int> CheckDuplicatePlayer(CommonParticipant participantRecord)
        {
            try
            {

                if (_playersCache.TryGetValue(participantRecord.Player.Id, out int databaseId) && databaseId != 0) return databaseId;
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"SELECT id FROM players WHERE startgg_link = @Input";
                    databaseId = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = participantRecord.Player.Id });
                    if (databaseId != 0) _playersCache.TryAdd(participantRecord.Player.Id, databaseId);

                    return databaseId;
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
            return 0;
        }
    }
}

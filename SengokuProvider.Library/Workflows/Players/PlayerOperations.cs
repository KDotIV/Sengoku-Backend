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
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Worker.Handlers;
using System.Collections.Concurrent;
using System.Text;

namespace SengokuProvider.Library.Workflows.Players
{
    public class PlayerOperations : IPlayerOperations
    {
        private readonly ICommonDatabaseService _commonDatabaseService;
        private readonly IPlayerQueryService _queryService;
        private readonly ILegendQueryService _legendQueryService;
        private readonly IEventQueryService _eventQueryService;
        private readonly IAzureBusApiService _azureBusApiService;
        private readonly IConfiguration _config;
        private readonly IPlayerIntakeService _intakeService;

        private readonly string _connectionString;
        private ConcurrentDictionary<int, int> _playersCache;
        private ConcurrentDictionary<int, string> _playerRegistry;
        private HashSet<int> _eventCache;
        private int _currentEventId;
        private static Random _rand = new Random();

        public PlayerOperations(string connectionString, IConfiguration configuration, ICommonDatabaseService commonServices, IPlayerQueryService playerQueryService,
            ILegendQueryService legendQueryService, IEventQueryService eventQueryService, IAzureBusApiService serviceBus, IPlayerIntakeService intakeService)
        {
            _connectionString = connectionString;
            _config = configuration;
            _commonDatabaseService = commonServices;
            _queryService = playerQueryService;
            _legendQueryService = legendQueryService;
            _eventQueryService = eventQueryService;
            _azureBusApiService = serviceBus;
            _intakeService = intakeService;
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
                var standingsSuccess = await _intakeService.IntakePlayerStandingData(mappedResult);

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
            PlayerData tempPlayerLink = await _queryService.GetPlayerDataById(playerId);
            var bracketData = await _queryService.QueryBracketDataFromStartggByBracketId(tempBracketId);
            BracketVictoryPathData processedData = await ProcessNewBracketData(bracketData, tempPlayerLink, tempTournamentId);

            onboardResult = await _intakeService.SaveVictoryPathData(processedData);

            return onboardResult;
        }
        private async Task<BracketVictoryPathData> ProcessNewBracketData(PhaseGroupGraphQL bracketData, PlayerData playerData, int tournamentId)
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
                        PlayerID = playerData.Id,
                        PlayerName = playerData.PlayerName,
                        PlayerResults = new List<PlayerStandingResult>()
                    },
                    EntrantSetCards = new List<EntrantSetCard>()
                };

                foreach (var setData in bracketData.PhaseGroup.Sets.Nodes)
                {
                    if (setData == null || setData.Slots == null || setData.Slots.Count < 2) continue;
                    var entrantOne = setData.Slots[0].Entrant;
                    var entrantTwo = setData.Slots[1].Entrant;
                    var entrantOnePlayerId = entrantOne?.Participants?.FirstOrDefault()?.Player?.Id;
                    var entrantTwoPlayerId = entrantTwo?.Participants?.FirstOrDefault()?.Player?.Id;

                    if(entrantOnePlayerId == playerData.PlayerLinkID || entrantTwoPlayerId == playerData.PlayerLinkID)
                    {
                        if(entrantOnePlayerId == entrantTwoPlayerId)
                        {
                            Console.WriteLine($"Entrant One and Two are the same: {entrantOne?.Name} - {entrantOne?.Id}");
                            continue;
                        }
                        result.PlayerTournamentCard.EntrantID = entrantOnePlayerId == playerData.PlayerLinkID ? entrantOne?.Id ?? 0 : entrantTwo?.Id ?? 0;
                        Console.WriteLine($"Player Entrant ID found: {result.PlayerTournamentCard.EntrantID} for Player: {playerData.PlayerName} in Tournament: {tournamentId}");
                        break;
                    }   
                }
                var tempPlayerArr = new int[] { playerData.Id };
                var tempTournamentArr = new int[] { tournamentId };
                PlayerStandingResult? firstRecord;
                List <PlayerStandingResult> playerStanding = await _queryService.GetStandingsDataByPlayerIds(tempPlayerArr, tempTournamentArr);
                if (playerStanding == null || playerStanding.Count == 0)
                {
                    firstRecord = new PlayerStandingResult
                    {
                        TournamentLinks = new Links
                        {
                            PlayerId = playerData.Id,
                            EntrantId = result.PlayerTournamentCard.EntrantID,
                        },
                        LastUpdated = DateTime.UtcNow,
                    };
                }
                else firstRecord = playerStanding.FirstOrDefault(x => x.TournamentLinks?.PlayerId == playerData.Id);

                //Pathfinder using found EntrantId
                SetNode? startingSet = FindStartingSet(bracketData.PhaseGroup.Sets.Nodes, result.PlayerTournamentCard.EntrantID);
                if (startingSet == null) { throw new ApplicationException("Unable to find starting set for the provided PlayerId"); }

                var playerPath = FindPath(bracketData.PhaseGroup.Sets.Nodes, startingSet.Id);
                playerPath.Insert(0, startingSet);

                var expectedOpponents = GetExpectedOpponents(bracketData.PhaseGroup.Sets.Nodes, playerPath, result.PlayerTournamentCard.EntrantID);

                var opponentCards = await BuildPlayerCardsFromOpponentData(expectedOpponents, result.PlayerTournamentCard, tournamentId);

                if (opponentCards == null || opponentCards.Count == 0) { throw new ApplicationException("Unable to Reduce Bracket data from Dataset with provided PlayerId"); }

                result.EntrantSetCards = opponentCards;

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine("BracketRunner error occurred: ", ex);
                throw;
            }
        }

        private async Task<List<EntrantSetCard>> BuildPlayerCardsFromOpponentData(List<ExpectedOpponent> expectedOpponents, PlayerTournamentCard playerTournamentCard, int tournamentId)
        {
            if(expectedOpponents == null || expectedOpponents.Count == 0) { return new List<EntrantSetCard>(); }

            if(playerTournamentCard == null || playerTournamentCard.PlayerID == 0) { return new List<EntrantSetCard>(); }
            
            List<EntrantSetCard> result = new List<EntrantSetCard>();

            var tempPlayerLinks = expectedOpponents.Select(x => x.PlayerLink).Distinct().ToList();

            var foundLegends = await _legendQueryService.GetLegendsByPlayerLink(tempPlayerLinks.ToArray());
            var messageJson = JsonConvert.SerializeObject(new OnboardLegendsByPlayerLinkCommand
            {
                Topic = CommandRegistry.OnboardPlayerToLeague,
                PlayerLinkIds = tempPlayerLinks.ToArray()
            }, JsonSettings.DefaultSettings);
            if (foundLegends == null || foundLegends.Count == 0) { await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:LegendReceivedQueue"], messageJson); }

            return result;
        }
        private List<SetNode> FindPath(IReadOnlyCollection<SetNode> nodes, string startingSetId, int requiredPlacement = 1)
        {
            var destinationsBySource = nodes
                .SelectMany(destination => (destination.Slots ?? [])
                    .Where(slot => !string.IsNullOrEmpty(slot.PrereqId))
                    .Select(slot => new
                    {
                        SourceSetId = slot.PrereqId!,
                        Destination = destination,
                        DestinationSlot = slot
                    }))
                .ToLookup(x => x.SourceSetId);

            var path = new List<SetNode>();
            var visited = new HashSet<string>();
            var currentSetId = startingSetId;

            while (visited.Add(currentSetId))
            {
                // Placement 1 means the winner of currentSetId feeds this slot.
                var edge = destinationsBySource[currentSetId]
                    .FirstOrDefault(x =>
                        x.DestinationSlot.PrereqPlacement == requiredPlacement);

                if (edge == null)
                    break;

                path.Add(edge.Destination);
                currentSetId = edge.Destination.Id;
            }

            return path;
        }
        private SetNode? FindStartingSet(IEnumerable<SetNode> nodes, int entrantId)
        {
            return nodes.Where(set => set.Slots?.Any(slot => slot.Entrant?.Id == entrantId) == true)
                        .OrderBy(set => set.Round > 0 ? set.Round : int.MaxValue)
                        .FirstOrDefault();
        }
        private List<ExpectedOpponent> GetExpectedOpponents(IReadOnlyCollection<SetNode> nodes, IReadOnlyList<SetNode> playerPath, int playerEntrantId)
        {
            // Duplicate set records can occur at page boundaries or in cached
            // upstream data. They represent the same bracket vertex, so retain
            // one record per non-empty set ID

            var setsById = nodes
                .Where(set => !string.IsNullOrWhiteSpace(set.Id))
                .GroupBy(set => set.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var opponents = new List<ExpectedOpponent>();
            string? previousPathSetId = null;

            foreach (var pathSet in playerPath)
            {
                var slots = pathSet.Slots ?? [];
                Slot? playerSideSlot;

                if(previousPathSetId == null)
                {
                    //Starting set: user may have been seeded here with free bye
                    playerSideSlot = slots.FirstOrDefault(slot => slot.Entrant?.Id == playerEntrantId);
                }
                else
                {
                    // Later set: this slot is sfed by preceding set in path
                    playerSideSlot = slots.FirstOrDefault(slot => slot.PrereqType == "set" && slot.PrereqId == previousPathSetId && slot.PrereqPlacement == 1);
                }

                if(playerSideSlot == null)
                { previousPathSetId = pathSet.Id; continue; }

                var opponentSlot = slots.FirstOrDefault(slot => slot.Id != playerSideSlot.Id);

                if(opponentSlot != null)
                {
                    var immediateSSsourceIdentifier = opponentSlot.PrereqId != null && setsById.TryGetValue(opponentSlot.PrereqId, out var immediatesource) ? immediatesource.Identifier : "Direct";

                    foreach (var candidate in GetPossibleEntrants(opponentSlot, setsById))
                    {
                        var particcipant = candidate.Entrant.Participants?.FirstOrDefault(x => x.Player != null);

                        if(particcipant?.Player == null) { continue; }

                        opponents.Add(new ExpectedOpponent(candidate.Entrant.Id, particcipant.Player.Id, particcipant.Player.GamerTag ?? "Unknown", pathSet.Identifier, candidate.SourcceIdentifier));
                    }
                }
                previousPathSetId = pathSet.Id;
            }
            return opponents.Where(x => x.EntrantId != playerEntrantId).DistinctBy(x => x.EntrantId).ToList();
        }
        private IEnumerable<(Entrant Entrant, string SourcceIdentifier)> GetPossibleEntrants(Slot slot, IReadOnlyDictionary<string, SetNode> setsById, HashSet<string>? visited = null)
        {
            //If slot is already populated, its entrant is authoritative
            if(slot.Entrant != null)
            {
                yield return (slot.Entrant, "Direct");
                yield break;
            }
            if(slot.PrereqType != "set" || string.IsNullOrWhiteSpace(slot.PrereqId) || !setsById.TryGetValue(slot.PrereqId, out var feederSet))
            {
                yield break;
            }

            visited ??= new HashSet<string>();

            if(!visited.Add(feederSet.Id))
                yield break; // Prevent cycles

            foreach(var feederSlot in feederSet.Slots ?? [])
            {
                foreach (var candidate in GetPossibleEntrants(feederSlot, setsById, visited))
                {
                    yield return (candidate.Entrant, feederSet.Identifier);
                }
            }

            visited.Remove(feederSet.Id);
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
            var result = await _intakeService.IntakePlayerStandingData(mappedStandings);
            return result;
        }
        private List<PlayerStandingResult> MapStandingsData(PlayerGraphQLResult? data)
        {
            List<PlayerStandingResult> mappedResult = new List<PlayerStandingResult>();
            if (data == null) return mappedResult;

            // Guard against null tournament/entrants/nodes to avoid CS8602
            if (data.TournamentLink?.Entrants?.Nodes == null || data.TournamentLink.Entrants.Nodes.Count == 0)
                return mappedResult;

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
        private async Task<int> ProcessPlayerData(PlayerGraphQLResult queryData)
        {
            var players = new List<PlayerData>();
            if (queryData.TournamentLink == null) throw new ApplicationException("Player Query Data was null from Start.gg");

            // Guard Entrants/Nodes before enumerating
            if (queryData.TournamentLink.Entrants?.Nodes == null || queryData.TournamentLink.Entrants.Nodes.Count == 0)
                return 0;

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
            return await _intakeService.InsertNewPlayerData(players);
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

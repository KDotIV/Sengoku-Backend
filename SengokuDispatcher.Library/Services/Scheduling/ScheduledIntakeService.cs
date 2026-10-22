using SengokuDispatcher.Library.Workflows.Events;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Players;
using SengokuDispatcher.Library.Workflows.Players;
using System.Text;
using System.Text.Json;

namespace SengokuDispatcher.Library.Services.Scheduling
{
    public class ScheduledIntakeService
    {
        private readonly IEventOperations _events;
        private readonly IEventQueryService _eventQueryService;
        private readonly ILegendQueryService _legendQueryService;
        private readonly ILegendIntakeService _legendIntakeService;
        private readonly IPlayerOperations _playerIntakeService;
        public ScheduledIntakeService(IEventOperations events, IEventQueryService eventQuery, ILegendQueryService legendQuery, ILegendIntakeService legendIntake,
            IPlayerOperations playerIntake)
        {
            _events = events;
            _eventQueryService = eventQuery;
            _legendQueryService = legendQuery;
            _legendIntakeService = legendIntake;
            _playerIntakeService = playerIntake;
        }
        public async Task RunTournamentUpdate(CancellationToken cancellationToken)
        {
            Console.WriteLine("Beginning TournamentStandingsUpdate");
            cancellationToken.ThrowIfCancellationRequested();
            var currentResults = await _legendQueryService.GetAvailableLeagues();

            var tempLeagueArr = new int[currentResults.Count];
            for (int i = 0; i < currentResults.Count; i++)
            {
                tempLeagueArr[i] = currentResults[i].LeagueId;
            }

            var currentTournaments = await _eventQueryService.GetTournamentsByLeagueIds(tempLeagueArr);
            int totalSuccess = 0;
            foreach (var tournament in currentTournaments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalSuccess += await _playerIntakeService.IntakePlayerData(tournament.Id);
            }
            Console.WriteLine($"TournamentStandingsUpdate complete with {totalSuccess} Players Data Inserted");
        }
        public async Task RunCircuitScheduleUpdate(CancellationToken cancellationToken)
        {
            Console.WriteLine("Beginning CircuitScheduleUpdate");
            var tempStartRange = DateTime.Today;
            var tempEndRange = DateTime.Today.AddDays(10);
            var currentResult = new CircuitUpdateScheduleResult
            {
                Success = new Dictionary<int, string>(),
                Errors = new Dictionary<int, string>()
            };

            var currentCircuits = await _legendQueryService.GetAllActiveLeagueRegions();

            foreach (var circuit in currentCircuits)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<AddressEventResult> currentEvents = await _eventQueryService.GetLocalEventsByLeagueRegions([circuit.LeagueId], circuit.Regions, 100);
                    var tempArr = new int[currentEvents.Count];
                    for (int i = 0; i < currentEvents.Count; i++)
                    {
                        tempArr[i] = currentEvents[i].EventLink;
                    }

                    var currentTournaments = await _eventQueryService.GetTournamentsByEventIds(tempArr);
                    if (currentTournaments.Count == 0)
                    {
                        currentResult.Errors.Add(circuit.LeagueId, "No Tournaments Found");
                        continue;
                    }

                    var tempHash = new HashSet<int>();
                    tempHash = currentTournaments.Where(tmnt => tmnt.GameId == circuit.GameId).Select(tmnt => tmnt.Id).ToHashSet();

                    var result = await _legendIntakeService.AddTournamentToLeague(tempHash.ToArray(), circuit.LeagueId);
                    if (result.Successful.Count > 0)
                    {
                        currentResult.Success.Add(circuit.LeagueId, $"{result.Successful.Count} Tournaments Successfully Added to League: {circuit.LeagueId}");
                    }
                    if (result.Failures.Count > 0)
                    {
                        currentResult.Errors.Add(circuit.LeagueId, $"{result.Failures.Count} Tournaments Failed to Add to League: {circuit.LeagueId} - {result.Response}");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    currentResult.Errors.Add(circuit.LeagueId, ex.Message);
                }
            }
            var tempJson = JsonSerializer.Serialize(currentResult);
            Console.WriteLine($"CircuitScheduleUpdate complete with Results:\n\n{tempJson}");
        }
        public async Task RunEventIntakeScheduler(CancellationToken cancellationToken)
        {
            Console.WriteLine("Beginning Scheduled Event Intake");
            var tempStartDate = DateTime.Today.AddDays(-2);
            var tempEndDate = DateTime.Today.AddDays(10);
            int startTimestamp = (int)(tempStartDate.Subtract(DateTime.UnixEpoch)).TotalSeconds;
            int endTimestamp = (int)(tempEndDate.Subtract(DateTime.UnixEpoch)).TotalSeconds;
            var currentResult = new TournamentSchedulerResult
            {
                Success = new Dictionary<string, string>(),
                Errors = new Dictionary<string, string>()
            };

            Console.WriteLine("Verifying States...");
            foreach (var state in EventRequestConstants.StateCodes)
            {
                Console.WriteLine($"Currently on: {state}");
                try
                {
                    var eventCmd = new IntakeEventsByLocationCommand
                    {
                        PerPage = 10,
                        StateCode = state,
                        StartDate = startTimestamp,
                        EndDate = endTimestamp,
                        Filters = EventRequestConstants.Filters,
                        VariableDefinitions = EventRequestConstants.VariableDefinitions,
                        Topic = CommandRegistry.IntakeEventsByLocation
                    };
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await _events.SendEventIntakeLocationMessage(eventCmd))
                    {
                        currentResult.Errors.Add(state, "The event command could not be dispatched.");
                    }
                    else
                    {
                        currentResult.Success.Add(state, "Queued");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    currentResult.Errors.Add(state, $"Error Intaking State: {ex.Message} - {ex.InnerException}");
                    continue;
                }
            }
            Console.WriteLine($"Intake Completed: Success: {currentResult.Success.Count} \n\n Failed: {currentResult.Errors.Count}");
        }
    }
}

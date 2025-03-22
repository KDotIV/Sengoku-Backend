using Microsoft.Azure.Functions.Worker;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using System.Text;
using System.Text.Json;
using TimerInfo = Microsoft.Azure.Functions.Worker.TimerInfo;
using TimerTriggerAttribute = Microsoft.Azure.Functions.Worker.TimerTriggerAttribute;

namespace EventTournamentScheduler
{
    public class EventIntakeScheduler
    {
        private readonly HttpClient _httpClient;
        private readonly TimerInfo _timerInfo;
        private readonly IEventQueryService _eventQueryService;
        private readonly ILegendQueryService _legendQueryService;
        private readonly ILegendIntakeService _legendIntakeService;
        public EventIntakeScheduler(HttpClient httpClient, IEventQueryService eventQuery, ILegendQueryService legendQuery, ILegendIntakeService legendIntake)
        {
            _httpClient = httpClient;
            _eventQueryService = eventQuery;
            _legendQueryService = legendQuery;
            _legendIntakeService = legendIntake;
        }
        [Function("TournamentStandingsUpdate")]
        public async Task RunTournamentUpdate([TimerTrigger("00 01,13 * * *")] TimerInfo schedule)
        {
            Console.WriteLine("Beginning TournamentStandingUpdate");

        }
        [Function("CircuitScheduleUpdate")]
        public async Task RunCircuitScheduleUpdate([TimerTrigger("00 02,13 * * *", RunOnStartup = true)] TimerInfo schedule)
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
                    List<AddressEventResult> currentEvents = await _eventQueryService.GetLocalEventsByLeagueRegions([circuit.LeagueId], circuit.Regions, 100);
                    var tempArr = new int[currentEvents.Count];
                    for (int i = 0; i < currentEvents.Count; i++)
                    {
                        tempArr[i] = currentEvents[i].LinkId;
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
                catch (Exception ex)
                {
                    currentResult.Errors.Add(circuit.LeagueId, ex.Message);
                }
            }
            var tempJson = JsonSerializer.Serialize(currentResult);
            Console.WriteLine($"CircuitScheduleUpdate complete with Results:\n\n{tempJson}");
        }
        [Function("EventIntakeScheduler")]
        public async Task RunEventIntakeScheduler([TimerTrigger("00 01,13 * * *")] TimerInfo schedule)
        {
            Console.WriteLine("Beginning Scheduled Event Intake");
            var tempStartDate = DateTime.Today;
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
                    HttpRequestMessage request = CreateHttpRequestMessage(eventCmd);
                    var response = await _httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        var tempJson = await response.Content.ReadAsStringAsync();
                        currentResult.Errors.Add(state, $"Error Intaking State: {response.StatusCode} - {tempJson}");
                    }
                    else
                    {
                        currentResult.Success.Add(state, "Success");
                    }
                }
                catch (Exception ex)
                {
                    currentResult.Errors.Add(state, $"Error Intaking State: {ex.Message} - {ex.InnerException}");
                    continue;
                }
            }
            Console.WriteLine($"Intake Completed: Success: {currentResult.Success.Count} \n\n Failed: {currentResult.Errors.Count}");
        }
        private static HttpRequestMessage CreateHttpRequestMessage(IntakeEventsByLocationCommand eventCmd)
        {
            return new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri($"{EventRequestConstants.sengokuBaseUrl}/api/events/IntakeEventsByLocation"),
                Content = new StringContent(JsonSerializer.Serialize(eventCmd), Encoding.UTF8, "application/json")
            };
        }
    }
}

using Microsoft.Azure.Functions.Worker;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using System.Text;
using System.Text.Json;
using TimerInfo = Microsoft.Azure.Functions.Worker.TimerInfo;
using TimerTriggerAttribute = Microsoft.Azure.Functions.Worker.TimerTriggerAttribute;

namespace EventTournamentScheduler
{
    public class EventIntakeScheduler
    {
        private readonly HttpClient _httpClient;
        public EventIntakeScheduler(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        [Function("EventIntakeScheduler")]
        public async Task Run([TimerTrigger("00 01,13 * * *", RunOnStartup = true)] TimerInfo schedule)
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

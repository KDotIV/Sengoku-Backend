using Microsoft.Azure.Functions.Worker;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using System.Net.Http;
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
        public async Task Run([TimerTrigger("00 01,13 * * *")] TimerInfo schedule)
        {
            var tempStartDate = DateTime.Today;
            var tempEndDate = DateTime.Today.AddDays(10);
            int startTimestamp = (int)(tempStartDate.Subtract(DateTime.UnixEpoch)).TotalSeconds;
            int endTimestamp = (int)(tempEndDate.Subtract(DateTime.UnixEpoch)).TotalSeconds;

            foreach (var state in EventRequestConstants.StateCodes)
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

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"{EventRequestConstants.sengokuBaseUrl}/api/events/IntakeEventsByLocation"),
                    Content = new StringContent(JsonSerializer.Serialize(eventCmd), Encoding.UTF8, "application/json")
                };
                await _httpClient.SendAsync(request);
            }
        }
    }
}

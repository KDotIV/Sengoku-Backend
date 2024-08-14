using Newtonsoft.Json;
using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Services.Comms
{
    public class DiscordWebhookHandler : IDiscordWebhookHandler
    {
        private readonly HttpClient _httpClient;
        public DiscordWebhookHandler()
        {
            _httpClient = new HttpClient();
        }
        public async Task SendThreadUpdateMessage(int gameId, string messageContent, long threadId, object? attachments = default)
        {
            using (var form = new MultipartFormDataContent())
            {
                var payload = new
                {
                    content = $"{messageContent}",
                };

                // Add JSON payload to the form
                form.Add(new StringContent(JsonConvert.SerializeObject(payload)), "payload_json");

                // Add the CSV file to the form
                /*var fileStream = new FileStream("", FileMode.Open, FileAccess.Read);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
                form.Add(fileContent, "file", "events.csv");*/

                // Send the request
                var response = await _httpClient.PostAsync($"{DiscordWebhookConfig.BaseWebhookUrl}?thread_id={threadId}", form);
                response.EnsureSuccessStatusCode();
            }
        }
    }
}

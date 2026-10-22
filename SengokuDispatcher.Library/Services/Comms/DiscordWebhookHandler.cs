using SengokuDispatcher.Library.Models.Common;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;

namespace SengokuDispatcher.Library.Services.Comms
{
    public class DiscordWebhookHandler : IDiscordWebhookHandler
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DiscordWebhookHandler> _logger;

        public DiscordWebhookHandler(HttpClient client, ILogger<DiscordWebhookHandler> logger)
        {
            _httpClient = client;
            _logger = logger;
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

                // Send the request
                using var response = await _httpClient.PostAsync($"{DiscordWebhookConfig.BaseWebhookUrl}?thread_id={threadId}", form);
                response.EnsureSuccessStatusCode();
            }
        }
        public async Task<bool> SendLeaderboardUpdateMessage(string webhookUrl, string messageContent, string[] roleIds, object? attachments = default)
        {
            try
            {
                using (var form = new MultipartFormDataContent())
                {
                    var payload = new
                    {
                        content = $"{messageContent}",
                        allowed_mentions = new
                        {
                            parse = new[] { "roles" },
                        }
                    };
                    // Add JSON payload to the form
                    form.Add(new StringContent(JsonConvert.SerializeObject(payload)), "payload_json");

                    // Send the request
                    using var response = await _httpClient.PostAsync($"{webhookUrl}", form);
                    response.EnsureSuccessStatusCode();
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Discord webhook request failed ({ErrorType})", ex.GetType().Name);
            }
            return false;
        }
    }
}

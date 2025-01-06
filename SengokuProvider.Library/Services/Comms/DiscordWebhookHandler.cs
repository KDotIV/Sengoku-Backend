using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Services.Comms
{
    public class DiscordWebhookHandler : IDiscordWebhookHandler
    {
        private readonly HttpClient _httpClient;
        private readonly string? _connection;
        private readonly Random _rand = new Random();

        public DiscordWebhookHandler(string? connection)
        {
            _httpClient = new HttpClient();
            _connection = connection;
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
                var response = await _httpClient.PostAsync($"{DiscordWebhookConfig.BaseWebhookUrl}?thread_id={threadId}", form);
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
                    var response = await _httpClient.PostAsync($"{webhookUrl}", form);
                    response.EnsureSuccessStatusCode();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message.ToString()} - {ex.StackTrace}");
            }
            return false;
        }
        public async Task<bool> SubscribeToFeed(string serverName, string subscribedChannel, string webhookUrl, string feedId)
        {
            if(string.IsNullOrEmpty(serverName) || string.IsNullOrEmpty(subscribedChannel) || string.IsNullOrEmpty(webhookUrl) || string.IsNullOrEmpty(feedId))
            {
                throw new ArgumentNullException("One or more parameters are null or empty.");
            }
            try
            {
                using (var conn = new NpgsqlConnection(_connection))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO subscribed_feeds (id, subscriber, subscribed_channel, 
                                                        webhook_url, feed_id, last_updated) VALUES (@InputId, @Subscriber, @Channel,
                                                        @Webhook, @FeedId, @LastUpdated) ON CONFLICT DO NOTHING", conn))
                    {
                        cmd.Parameters.AddWithValue("@InputId", _rand.Next(100000, 1000000));
                        cmd.Parameters.AddWithValue("@Subscriber", serverName);
                        cmd.Parameters.AddWithValue("@Channel", subscribedChannel);
                        cmd.Parameters.AddWithValue("@Webhook", webhookUrl);
                        cmd.Parameters.AddWithValue("@FeedId", feedId);
                        cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);

                        var result = await cmd.ExecuteNonQueryAsync();
                        if (result > 0)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Database error occurred: ", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unexpected Error Occurred: ", ex);
            }
        }
    }
}

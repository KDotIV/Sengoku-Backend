namespace SengokuProvider.Library.Services.Comms
{
    public interface IDiscordWebhookHandler
    {
        public Task SendThreadUpdateMessage(int gameId, string messageContent, long theadId, object? attachments = default);
    }
}
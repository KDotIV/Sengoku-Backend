namespace SengokuProvider.Library.Models.Common
{
    public class SendLeaderboardUpdateCommand : ICommand
    {
        public required string WebhookUrl { get; set; }
        public required string MessageContent { get; set; }
        public required string[] RoleMentionIds { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (RoleMentionIds.Length > 0 && !string.IsNullOrEmpty(MessageContent)) return true;
            return false;
        }
    }
}

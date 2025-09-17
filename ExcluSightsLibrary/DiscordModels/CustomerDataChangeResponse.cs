namespace ExcluSightsLibrary.DiscordModels
{
    public sealed class CustomerDataChangeResponse
    {
        public required string CustomerId { get; set; }
        public required ulong DiscordId { get; set; }
        public required string ResponseMessage { get; set; }
    }
}

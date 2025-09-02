namespace ExcluSightsLibrary.DiscordModels
{
    public class CustomerProfileData
    {
        public required string CustomerId { get; set; }
        public required ulong DiscordId { get; set; }
        public required string DiscordTag { get; set; }
        public string? CustomerFirstName { get; set; }
        public string? CustomerLastName { get; set; }
        public required DateTime LastUpdated { get; set; }
    }
}

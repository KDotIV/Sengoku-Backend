namespace ExcluSightsLibrary.DiscordModels
{
    public class CustomerReportRow
    {
        public string CustomerId { get; init; } = default!;
        public ulong DiscordId { get; init; }
        public string DiscordTag { get; init; } = default!;
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public int? Gender { get; init; }
        public double? ShoeSize { get; init; }
    }
}

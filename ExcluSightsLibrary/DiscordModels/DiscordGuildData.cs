namespace ExcluSightsLibrary.DiscordModels
{
    public sealed class DiscordGuildData
    {
        public required ulong ServerGuildId { get; init; }
        public required string ServerName { get; init; }
        public int? MemberCount { get; set; }
        public DateTimeOffset LastUpdatedUTC { get; set; }
    }
}

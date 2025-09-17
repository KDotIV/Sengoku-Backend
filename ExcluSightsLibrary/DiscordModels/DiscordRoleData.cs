namespace ExcluSightsLibrary.DiscordModels
{
    public sealed class DiscordRoleData
    {
        public required ulong RoleId { get; init; }
        public required string RoleName { get; init; }
        public bool IsManaged { get; init; }
        public DateTimeOffset LastUpdatedUTC { get; set; }
    }
}
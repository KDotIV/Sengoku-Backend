namespace ExcluSightsLibrary.DiscordModels
{
    public sealed record RoleMapping(
        ulong GuildId,
        ulong RoleId,
        string RoleName,
        int? Gender = null,
        double? ShoeSize = null,
        Interests? Interests = null
    );
}

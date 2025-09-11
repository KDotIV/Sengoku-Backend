namespace ExcluSightsLibrary.DiscordModels
{
    public sealed record RoleMapping(
        ulong GuildId,
        ulong RoleId,
        int? Gender = null,
        double? ShoeSize = null,
        Interests? Interests = null
    );

}

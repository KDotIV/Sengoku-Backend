namespace ExcluSightsLibrary.DiscordModels
{
    public class SolePlayDTO : CustomerProfileData
    {
        public required double ShoeSize { get; set; }
        public required Interests Interests { get; set; }
        public required int Gender { get; set; }
    }
    public enum Interests
    {
        None = 0,
        Gaming = 1,
        Fashion = 2,
        Other = 16
    }
}

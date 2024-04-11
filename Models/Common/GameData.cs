namespace SengokuProvider.API.Models.Common
{
    public class GameData
    {
        public required int Id { get; set; }
        public required string GameName { get; set; }
    }
    public enum Game
    {
        StreetFighter = 1,
        GuiltyGear = 2,
        MarvelVsCapcom = 3,
        None = 0
    }
}

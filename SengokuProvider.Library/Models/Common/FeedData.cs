namespace SengokuProvider.Library.Models.Common
{
    public class FeedData
    {
        public required string FeedId { get; set; }
        public required FeedType FeedType { get; set; }
        public required string FeedName { get; set; }
        public required string UserOwner { get; set; }
        public required int UserId { get; set; }
        public required DateTime LastUpdated { get; set; }
    }
    public enum FeedType
    {
        Game,
        League = 201,
        Tournament,
        Player,
        Team,
        Match,
        Round,
        Season,
        Event,
        News,
        Announcement,
        Update,
        Error
    }
}

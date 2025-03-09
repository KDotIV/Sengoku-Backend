namespace SengokuProvider.Library.Models.User
{
    public class UserPlayerData
    {
        public required int PlayerId { get; set; }
        public required string PlayerName { get; set; }
        public required string PlayerEmail { get; set; }
        public required int userLink { get; set; }
        public required int[] GameIds { get; set; }
    }
    public class UserPlayerDataResponse
    {
        public UserPlayerData? Data { get; set; }
        public required string Response { get; set; }
    }
}

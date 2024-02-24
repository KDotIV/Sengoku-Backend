namespace SengokuProvider.API.Models
{
    public class UserData
    {
        public required int Id { get; set; }
        public required string UserName { get; set; }
        public required string Password { get; set; }
        public required string Email { get; set; }
    }
}

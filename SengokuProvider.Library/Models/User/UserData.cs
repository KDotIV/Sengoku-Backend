namespace SengokuProvider.Library.Models.User
{
    public class UserData
    {
        public required int Id { get; set; }
        public required string UserName { get; set; }
        public string? DisplayName { get; set; }
        public required string Password { get; set; }
        public required string Email { get; set; }
        public required string PermissionChecksum { get; set; }
    }

    public class UserPermissions
    {
        public int Id { get; set; }
        public required UserType UserType { get; set; }
    }
    public enum UserType
    {
        Admin = 0,
        Client = 1,
        Standard = 2
    }
}

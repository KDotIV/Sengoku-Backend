using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.User
{
    public class CreateUserCommand : ICommand
    {
        public required string UserName { get; set; }
        public required string Password { get; set; }
        public required string Email { get; set; }
        public CommandRegistry Topic { get; set; } = CommandRegistry.CreateNewUser;
        public string? Response { get; set; }

        public bool Validate()
        {
            return !string.IsNullOrEmpty(UserName) &&
                   !string.IsNullOrEmpty(Password) &&
                   !string.IsNullOrEmpty(Email);
        }
    }
    public class SyncStartggToUserCommand : ICommand
    {
        public required string PlayerName { get; set; }
        public required string UserSlug { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            return !string.IsNullOrEmpty(PlayerName) ||
                !string.IsNullOrEmpty(UserSlug);
        }
    }
    public class DeleteUserCommand : ICommand
    {
        public required int UserId { get; set; }
        public required string UserName { get; set; }
        public required CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            return !string.IsNullOrEmpty(UserName) && UserId > 0;
        }
    }
    public class UpdateUserCommand : ICommand
    {
        public required int UserId { get; set; }
        public required Tuple<string, string>[]? UpdateDefinitions { get; set; }
        public required CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
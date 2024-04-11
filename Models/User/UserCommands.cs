using SengokuProvider.API.Models.Common;

namespace SengokuProvider.API.Models.User
{
    public class CreateUserCommand : ICommand
    {
        public required string UserName { get; set; }
        public required string Password { get; set; }
        public required string Email { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            return !string.IsNullOrEmpty(UserName) &&
                   !string.IsNullOrEmpty(Password) &&
                   !string.IsNullOrEmpty(Email);
        }
    }
    public class DeleteUserCommand : ICommand
    {
        public required int UserId { get; set; }
        public required string UserName { get; set; }
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
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
using SengokuProvider.API.Models.Common;

namespace SengokuProvider.API.Services.Common
{
    public class CommandProcessor
    {
        public CommandProcessor()
        {
        }

        public Task<T> ParseRequest<T>(T command) where T : ICommand
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command), "Command cannot be empty");
            }

            if (!command.Validate())
            {
                command.Response = "BadRequest: Validation failed";
                return Task.FromResult(command);
            }

            command.Response = "Success";
            return Task.FromResult(command);
        }
    }
}

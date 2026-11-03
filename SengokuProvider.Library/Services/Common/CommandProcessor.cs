using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Services.Common
{
    public class CommandProcessor
    {
        public CommandProcessor()
        {
        }

        public Task<T> ParseRequest<T>(T command) where T : ICommand
        {
            using var operation = Sengoku.Observability.OperationLog.Step("Command.Validate");
            if (command == null)
            {
                operation.Reject("CommandMissing");
                throw new ArgumentNullException(nameof(command), "Command cannot be empty");
            }

            if (!command.Validate())
            {
                operation.Reject("ValidationFailed");
                command.Response = "BadRequest: Validation failed";
                return Task.FromResult(command);
            }

            command.Response = "Success";
            operation.Complete();
            return Task.FromResult(command);
        }
    }
}

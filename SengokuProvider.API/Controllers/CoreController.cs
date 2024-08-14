using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Comms;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/core/")]
    public class CoreController
    {
        private readonly ILogger<CoreController> _logger;
        private readonly CommandProcessor _commandProcessor;
        private readonly ICommonDatabaseService _commonDb;
        private readonly IDiscordWebhookHandler _discordWebhook;

        public CoreController(ILogger<CoreController> logger, ICommonDatabaseService commonDb, IDiscordWebhookHandler webHookHandler, CommandProcessor commandProcessor)
        {
            _logger = logger;
            _commonDb = commonDb;
            _commandProcessor = commandProcessor;
            _discordWebhook = webHookHandler;
        }
        [HttpGet("Pulse")]
        public async Task<IActionResult> Pulse()
        {
            await _discordWebhook.SendThreadUpdateMessage(636751, "Testing... Testing...", 1273004220831891517);
            return new OkObjectResult("I'm Alive...");
        }
        [HttpPost("CreateTable")]
        public async Task<IActionResult> CreateDatabaseTable([FromBody] CreateTableCommand command)
        {
            if (command == null)
            {
                _logger.LogError("CreateTable command is null.");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }

            var parsedRequest = await _commandProcessor.ParseRequest(command);

            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _logger.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _commonDb.CreateTable(parsedRequest.TableName, parsedRequest.TableDefinitions);
                if (result > 0)
                {
                    return new OkObjectResult($"Table {parsedRequest.TableName} created successfully.");
                }

                _logger.LogError("CreateTable execution failed.");
                return new ObjectResult("Unexpected Error Occured");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating table.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

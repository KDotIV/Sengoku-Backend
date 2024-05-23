using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/players/")]
    public class PlayerController : Controller
    {
        private readonly ILogger<PlayerController> _log;
        private readonly IPlayerIntakeService _playerIntakeService;
        private readonly IPlayerQueryService _playerQueryService;
        private readonly CommandProcessor _commandProcessor;

        public PlayerController(ILogger<PlayerController> logger, IPlayerIntakeService intakeService, IPlayerQueryService queryService,
            CommandProcessor commandProcessor)
        {
            _log = logger;
            _playerIntakeService = intakeService;
            _playerQueryService = queryService;
            _commandProcessor = commandProcessor;
        }
        [HttpPost("IntakePlayersByTournament")]
        public async Task<IActionResult> IntakePlayersByTournament([FromBody] IntakePlayersByTournamentCommand command)
        {
            if(command == null)
            {
                _log.LogError("Command cannot be empty or null");
                return new BadRequestObjectResult("Command cannot be null") { StatusCode = StatusCodes.Status400BadRequest };
            }

            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if(!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"REquest parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _playerIntakeService.IntakePlayerData(command);
                return new OkObjectResult($"Players Inserted: {result}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Player Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
    }
}

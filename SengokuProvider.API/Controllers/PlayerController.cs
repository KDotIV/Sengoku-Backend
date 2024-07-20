using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
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
            if (command == null)
            {
                _log.LogError("Command cannot be empty or null");
                return new BadRequestObjectResult("Command cannot be null") { StatusCode = StatusCodes.Status400BadRequest };
            }

            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _playerIntakeService.SendPlayerIntakeMessage(command.EventSlug, command.PerPage, command.PageNum);
                if (result) { return new OkObjectResult($"Player Intake Successful"); }
                else { return new ObjectResult($"Failed to Intake Player with Event: {command.EventSlug}"); }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Player Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpGet("QueryPlayerStandings")]
        public async Task<IActionResult> QueryPlayerStandingsByEventId([FromBody] QueryPlayerStandingsCommand command)
        {
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }
            try
            {
                var result = await _playerQueryService.GetPlayerStandingResults(parsedRequest);
                if (result.Count == 0)
                {
                    return new OkObjectResult($"No Standings exist for this Player");
                }
                var resultJson = JsonConvert.SerializeObject(result);
                return new OkObjectResult($"{resultJson}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

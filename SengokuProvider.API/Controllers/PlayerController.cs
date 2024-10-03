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
        [HttpPost("GetRegisteredPlayersByTournamentId")]
        public async Task<IActionResult> GetRegisteredPlayersByTournamentId([FromBody] GetRegisteredPlayersByTournamentIdCommand command)
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
                var result = await _playerQueryService.GetRegisteredPlayersByTournamentId(command.TournamentLink);
                if (result.Count == 0)
                {
                    return new ObjectResult($"No PlayerData found") { StatusCode = StatusCodes.Status404NotFound };
                }
                var resultJson = JsonConvert.SerializeObject(result);
                return new OkObjectResult($"{resultJson}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Player Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
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
                var result = await _playerIntakeService.SendPlayerIntakeMessage(command.TournamentLink);
                if (result) { return new OkObjectResult($"Player Intake Successful"); }
                else { return new ObjectResult($"Failed to Intake Player with TournamentLink: {command.TournamentLink}"); }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Player Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpPost("OnboardPreviousTournamentDataByPlayer")]
        public async Task<IActionResult> OnboardPreviousTournamentDataByPlayer([FromBody] OnboardPlayerDataCommand command)
        {
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }
            try
            {
                var result = await _playerIntakeService.OnboardPreviousTournamentData(command);
                return new OkObjectResult($"Total Successful Tournament Data Inserted: {result}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("GetPlayerStandings")]
        public async Task<IActionResult> GetPlayerStandingsByPlayerId([FromBody] GetPlayerStandingsCommand command)
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
                    return new ObjectResult($"No Standings exist for this Player") { StatusCode = StatusCodes.Status404NotFound };
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

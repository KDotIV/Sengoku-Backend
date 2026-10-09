using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Workflows.Players;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/players/")]
    public class PlayerController : Controller
    {
        private readonly ILogger<PlayerController> _log;
        private readonly IPlayerOperations _playerOpService;
        private readonly IPlayerQueryService _playerQueryService;
        private readonly CommandProcessor _commandProcessor;

        public PlayerController(ILogger<PlayerController> logger, IPlayerOperations intakeService, IPlayerQueryService queryService,
            CommandProcessor commandProcessor)
        {
            _log = logger;
            _playerOpService = intakeService;
            _playerQueryService = queryService;
            _commandProcessor = commandProcessor;
        }
        [HttpGet("GetRegisteredPlayersByTournamentId")]
        public async Task<IActionResult> GetRegisteredPlayersByTournamentId([FromQuery] int[] tournamentLinks)
        {
            if (tournamentLinks == null || tournamentLinks.Length == 0) return BadRequest("Tournament Request cannot be null");
            try
            {
                var result = await _playerQueryService.GetRegisteredPlayersByTournamentId(tournamentLinks);
                if (result.Count == 0)
                {
                    return new ObjectResult($"No PlayerData found") { StatusCode = StatusCodes.Status404NotFound };
                }
                return Ok(result);
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
                var result = await _playerOpService.IntakePlayerData(command.TournamentLink);
                if (result == 0) { return new OkObjectResult($"No New Standings to Add for Tournament: {command.TournamentLink}"); }
                if (result > 0) { return new OkObjectResult($"{result} Successful Player Stadings Added"); }
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
                var result = await _playerOpService.OnboardPreviousTournamentData(command);
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
        [HttpGet("GetTournamentCardsByPlayerIDs")]
        public async Task<IActionResult> GetTournamentCardsByPlayerIDs([FromQuery] int[] playerIds)
        {
            if (playerIds.Length < 1) return new OkObjectResult("Must have at least 1 playerId to get Tournament Cards");

            try
            {
                var result = await _playerQueryService.GetTournamentCardsByPlayerIDs(playerIds);
                return Ok();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpGet("BracketProcessing/{operationId:guid}")]
        public async Task<IActionResult> GetBracketProcessingStatus(Guid operationId)
        {
            var result = await _playerOpService.GetBracketProcessingStatus(operationId);
            return result == null ? NotFound() : Ok(result);
        }
        [HttpPost("OnboardBracketPathByBracketSlug")]
        public async Task<IActionResult> OnboardBracketPathByBracketSlug([FromBody] OnboardBracketPathByBracketSlug command)
        {
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }
            try
            {
                var result = await _playerOpService.OnboardBracketPathByBracketSlug(command.BracketSlug, command.PlayerId);
                if (result.Status == "Pending")
                    return AcceptedAtAction(nameof(GetBracketProcessingStatus), new { operationId = result.OperationId }, result);
                if (result.Status is "Failed" or "Expired" || result.Response.StartsWith("FAILED:", StringComparison.Ordinal))
                    return BadRequest(result);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpGet("GetBracketPathByPlayerId")]
        public async Task<IActionResult> GetBracketPathByPlayerId([FromQuery] int playerId)
        {
            try
            {
                var result = await _playerQueryService.GetBracketPathByPlayerId(playerId);
                if (result == null)
                {
                    return NotFound($"No Bracket Path found for Player ID: {playerId}");
                }
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Bracket Path Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

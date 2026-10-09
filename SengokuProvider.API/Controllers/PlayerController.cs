using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
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
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [Authorize]
        [HttpGet("BracketProcessing/{operationId:guid}")]
        public async Task<IActionResult> GetBracketProcessingStatus(Guid operationId, [FromServices] IBracketCheckpointStore checkpoints)
        {
            var checkpoint = await checkpoints.GetAsync(operationId);
            if (checkpoint == null || checkpoint.Data.PlayerTournamentCard.PlayerID.ToString() != User.FindFirstValue("player_id"))
                return NotFound();
            var result = await _playerOpService.GetBracketProcessingStatus(operationId);
            return result == null ? NotFound() : Ok(result);
        }
        [Authorize, ValidateAntiForgeryToken]
        [HttpPost("BracketProcessing/{operationId:guid}/Retry")]
        public async Task<IActionResult> RetryBracketProcessing(Guid operationId, [FromServices] IBracketCheckpointStore checkpoints, [FromServices] SengokuProvider.API.Authentication.StartggOAuthService oauth, CancellationToken cancellationToken)
        {
            var checkpoint = await checkpoints.GetAsync(operationId);
            if (checkpoint == null || checkpoint.Data.PlayerTournamentCard.PlayerID.ToString() != User.FindFirstValue("player_id")) return NotFound();
            if (await oauth.GetLink(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken) == null)
                return StatusCode(403, new { response = "Verify your Start.gg account before retrying a bracket import." });
            var result = await _playerOpService.RetryBracketProcessing(operationId);
            if (result == null) return NotFound();
            return result.Status == "Pending" ? AcceptedAtAction(nameof(GetBracketProcessingStatus), new { operationId }, result) : Ok(result);
        }
        [Authorize]
        [ValidateAntiForgeryToken]
        [HttpPost("OnboardBracketPathByBracketSlug")]
        public async Task<IActionResult> OnboardBracketPathByBracketSlug([FromBody] OnboardBracketPathByBracketSlug command, [FromServices] SengokuProvider.API.Authentication.StartggOAuthService oauth, CancellationToken cancellationToken)
        {
            if (command == null || command.PlayerId.ToString() != User.FindFirstValue("player_id")) return Forbid();
            if (await oauth.GetLink(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken) == null)
                return StatusCode(403, new { response = "Verify your Start.gg account before importing a bracket." });
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
                return StatusCode(500, new { response = "Bracket import failed. Please try again later." });
            }
        }
        [HttpGet("GetBracketPathByPlayerId")]
        public async Task<IActionResult> GetBracketPathByPlayerId([FromQuery] int playerId)
        {
            try
            {
                var result = await _playerQueryService.GetBracketPathByPlayerId(playerId);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Bracket Path Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [AllowAnonymous]
        [HttpGet("GetBracketPathByPlayerName")]
        public async Task<IActionResult> GetBracketPathByPlayerName([FromQuery] string playerName)
        {
            try { return Ok(await _playerQueryService.GetBracketPathByPlayerName(playerName)); }
            catch (ArgumentException ex) { return BadRequest(new { response = ex.Message }); }
        }
        [AllowAnonymous]
        [HttpGet("GetBracketPathByTournamentSlug")]
        public async Task<IActionResult> GetBracketPathByTournamentSlug([FromQuery] string tournamentSlug)
        {
            try { return Ok(await _playerQueryService.GetBracketPathByTournamentSlug(tournamentSlug)); }

            catch (ArgumentException ex) { return BadRequest(new { response = ex.Message }); }
        }
        [HttpGet("GetBracketPathByPlayerIds")]
        public async Task<IActionResult> GetBracketPathByPlayerIds([FromQuery] int[] playerIds)
        {
            try
            {
                var results = await _playerQueryService.GetBracketPathByPlayerIds(playerIds);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error querying bracket paths for multiple players.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error querying bracket paths.");
            }
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Comms;
using SengokuProvider.Library.Services.Legends;

namespace SengokuProvider.API.Controllers
{
    [Route("api/leagues/")]
    [ApiController]
    public class LeagueController : Controller
    {
        private readonly ILogger<LeagueController> _log;
        private readonly ILegendIntakeService _legendIntakeService;
        private readonly ILegendQueryService _legendQueryService;
        private readonly IDiscordWebhookHandler _webhookHandler;
        private readonly CommandProcessor _commandProcessor;

        public LeagueController(ILogger<LeagueController> logger, ILegendIntakeService legendIntake, ILegendQueryService legendQuery,
            IDiscordWebhookHandler webhookHandler, CommandProcessor command)
        {
            _log = logger;
            _legendIntakeService = legendIntake;
            _legendQueryService = legendQuery;
            _webhookHandler = webhookHandler;
            _commandProcessor = command;
        }
        [HttpPost("SendLeaderboardUpdateMessage")]
        public async Task<IActionResult> SendLeaderboardUpdateMessage([FromBody] SendLeaderboardUpdateCommand command)
        {
            if (command == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _webhookHandler.SendLeaderboardUpdateMessage(command.WebhookUrl, command.MessageContent, command.RoleMentionIds);
                return new OkObjectResult(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("GetLeaderboardResultsByLeagueId")]
        public async Task<IActionResult> GetLeaderboardResultsByLeagueId([FromBody] GetLeaderboardResultsByLeagueCommand command)
        {
            if (command == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }
            try
            {
                List<LeaderboardData> result = await _legendQueryService.GetLeaderboardResultsByLeagueId(command.LeagueId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("AddTournamentToLeague")]
        public async Task<IActionResult> AddTournamentToLeague([FromBody] OnboardTournamentToLeagueCommand command)
        {
            if (command == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _legendIntakeService.AddTournamentToLeague(command.TournamentIds, command.LeagueId);
                return new OkObjectResult(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpPost("AddPlayerToLeague")]
        public async Task<IActionResult> AddPlayerToLeague([FromBody] OnboardPlayerToLeagueCommand command)
        {
            if (command == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _legendIntakeService.AddPlayerToLeague(command.PlayerIds, command.LeagueId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpPost("GetLeaderboardsByOrg")]
        public async Task<IActionResult> GetLeaderboardsByOrgId([FromBody] GetLeaderboardsByOrgCommand command)
        {
            if (command == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _legendQueryService.GetLeaderboardsByOrgId(command.OrgId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Leaderboard Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("CreateLeagueByOrg")]
        public async Task<IActionResult> CreateLeagueByOrg([FromBody] CreateLeagueByOrgCommand command)
        {
            if (command == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                LeagueByOrgResults result = await _legendIntakeService.InsertNewLeagueByOrg(command.OrgId, command.LeagueName, command.StartDate, command.EndDate, command.GameId, command.Description);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Leaderboard Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

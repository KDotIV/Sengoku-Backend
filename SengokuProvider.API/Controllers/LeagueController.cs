using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
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
        [HttpGet("GetLeaderboardResultsByLeagueId")]
        public async Task<IActionResult> GetLeaderboardResultsByLeagueId([FromQuery] int[] leagueIds, [FromQuery] int topN)
        {
            try
            {
                List<LeaderboardData> result = await _legendQueryService.GetLeaderboardResultsByLeagueId(leagueIds, topN);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpGet("GetLeagueTournamentSchedule")]
        public async Task<IActionResult> GetLeagueTournamentScheduleByLeagueId([FromQuery] int[] leagueIds)
        {
            try
            {
                List<LeagueTournamentData> result = await _legendQueryService.GetLeagueTournamentScheduleByLeagueId(leagueIds);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Finding Tournament Schedule");
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
        [HttpPut("UpdateLeaderboardStandingsLeague")]
        public async Task<IActionResult> UpdateLeaderboardStandingsByLeagueId([FromBody] UpdateLeaderboardStandingsByLeague command)
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
                var result = await _legendIntakeService.UpdateLeaderboardStandingsByLeagueId(command.LeagueIds);
                return new OkObjectResult(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Updating Leaderboard");
                return new ObjectResult($"Error Message: {ex.Message}") { StatusCode = StatusCodes.Status500InternalServerError };
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
        [HttpPost("RegisterUserToLeague")]
        public async Task<IActionResult> AddUserToLeague([FromBody] OnboardUserToLeagueCommand cmd)
        {
            if (cmd == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(cmd);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }
            try
            {
                bool result = await _legendIntakeService.AddUserToLeague(cmd.PlayerId, cmd.PlayerName, cmd.PlayerEmail, cmd.LeagueId, cmd.GameIds);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("OnboardTournamentStandingstoLeague")]
        public async Task<IActionResult> OnboardTournamentStandingstoLeague([FromBody] OnboardTournamentStandingstoLeague command)
        {
            if (command == null)
            {
                _log.LogError("Command was null");
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
                var results = await _legendIntakeService.IntakeTournamentStandingsByEventLink(command.TournamentLinks, command.EventLinkSlug, command.GameIds, command.LeagueId, command.Open);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpGet("GetLeaguesByOrgId")]
        public async Task<IActionResult> GetLeaguesByOrgId([FromQuery] int orgId)
        {
            var command = new GetLeaderboardsByOrgCommand { OrgId = orgId };
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
        [HttpGet("GetLeaguesByLeagueIds")]
        public async Task<IActionResult> GetLeaguesByLeagueIds([FromQuery] int[] leagueIds)
        {
            try
            {
                var result = await _legendQueryService.GetLeagueByLeagueIds(leagueIds);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying League Data.");
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
        [HttpPost("CreateNewRunnerBoard")]
        public async Task<IActionResult> CreateNewRunnerBoard([FromBody] CreateNewRunnerBoardCommand command)
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
                var result = await _legendIntakeService.CreateNewRunnerBoard(command.TournamentIds, command.UserId, command.UserName, command.OrgId, command.OrgName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Adding Bracket check IDs");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpGet("GetCurrentRunnerBoard")]
        public async Task<IActionResult> GetCurrentRunnerBoard([AsParameters] GetCurrentRunnerBoardByUserCommand command)
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
                List<TournamentBoardResult> result = await _legendQueryService.GetCurrentRunnerBoard(command.UserId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Retrieving RunnerBoard");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPut("AddTournamentToRunnerBoard")]
        public async Task<IActionResult> AddTournamentToRunnerBoard([FromBody] AddTournamentToRunnerBoardCommand command)
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
                var result = await _legendIntakeService.AddTournamentsToRunnerBoard(command.UserId, command.OrgId, command.TournamentIds);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Adding Tournament to RunnerBoard");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpGet("GetAvailableLeagues")]
        public async Task<IActionResult> GetAvailableLeagues()
        {
            try
            {
                var result = await _legendQueryService.GetAvailableLeagues();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Fetching League Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("AddLeagueToUser")]
        public async Task<IActionResult> AddLeagueToUser([FromQuery] int leagueId, [FromQuery] int userId)
        {
            try
            {
                bool result = await _legendIntakeService.AddLeagueToUser(leagueId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Registering League to User.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

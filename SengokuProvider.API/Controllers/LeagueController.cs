using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Services.Common;
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
        private readonly CommandProcessor _commandProcessor;

        public LeagueController(ILogger<LeagueController> logger, ILegendIntakeService legendIntake, ILegendQueryService legendQuery, CommandProcessor command)
        {
            _log = logger;
            _legendIntakeService = legendIntake;
            _legendQueryService = legendQuery;
            _commandProcessor = command;
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
                var result = await _legendIntakeService.AddTournamentToLeague(command.TournamentId, command.LeagueId);
                return new OkObjectResult(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
    }
}

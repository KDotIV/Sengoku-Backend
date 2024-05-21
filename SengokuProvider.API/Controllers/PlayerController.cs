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

            return new OkObjectResult($"Players Inserted: ");
        }
    }
}

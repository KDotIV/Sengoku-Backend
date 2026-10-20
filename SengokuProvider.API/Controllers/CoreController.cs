using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Comms;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/core/")]
    public class CoreController : Controller
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
        public IActionResult Pulse()
        {
            return new OkObjectResult("I'm Alive...");
        }
    }
}

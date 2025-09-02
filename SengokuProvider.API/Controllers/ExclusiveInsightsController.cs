using ExcluSightsLibrary.DiscordServices;
using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/exclusights/")]
    public class ExclusiveInsightsController : Controller
    {
        private readonly ILogger<ExclusiveInsightsController> _log;
        private readonly ISocketEngine _socketEngine;
        private readonly CommandProcessor _commandProcessor;
        public ExclusiveInsightsController(ILogger<ExclusiveInsightsController> logger, ISocketEngine socketEngine, CommandProcessor processor)
        {
            _log = logger;
            _commandProcessor = processor;
            _socketEngine = socketEngine;
        }
    }
}

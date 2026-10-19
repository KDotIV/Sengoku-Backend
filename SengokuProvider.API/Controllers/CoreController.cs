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
        // Legacy subscriptions had no account ownership or protected webhook storage.
        [HttpPost("SubscribeDiscordWebhookToFeed")]
        public IActionResult CreateFeedToDiscordWebhook() => StatusCode(StatusCodes.Status410Gone,
            new { response = "Use the authenticated /api/event-feeds endpoints to configure Discord event digests." });
        [HttpGet("GetFeedsByType")]
        public async Task<IActionResult> GetFeedsByType([FromQuery] int feedType)
        {
            throw new NotImplementedException();
        }
        [HttpGet("GetFeedById")]
        public async Task<IActionResult> GetFeedById([FromQuery] string feedId)
        {
            if (string.IsNullOrEmpty(feedId))
            {
                _logger.LogError("FeedId cannot be null or empty.");
                return new BadRequestObjectResult("FeedId cannot be null or empty.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            try
            {
                FeedData result = await _discordWebhook.GetFeedById(feedId);
                if (result.UserId > 0)
                {
                    return new OkObjectResult(result);
                }
                return new NotFoundObjectResult("No Feed found under that Id.") { StatusCode = StatusCodes.Status404NotFound };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting feed by id.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

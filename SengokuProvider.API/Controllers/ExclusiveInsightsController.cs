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
        private readonly EventListenerManager _eventManager;
        private readonly CommandProcessor _commandProcessor;
        public ExclusiveInsightsController(ILogger<ExclusiveInsightsController> logger, EventListenerManager manager, CommandProcessor processor)
        {
            _log = logger;
            _commandProcessor = processor;
            _eventManager = manager;
        }
        [HttpPost("AddDiscordServerListener")]
        public async Task<IActionResult> AddDiscordServerListener([FromQuery] ulong guildId)
        {
            try
            {
                _log.LogInformation("Received request to initialize Discord server listener.");
                var result = await _eventManager.AddDiscordWebSocketListener(guildId);
                if (!result) return NotFound($"Bot is not in Server {guildId} or failed to connect.");
                return Ok("Discord server listener initialized successfully.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error initializing Discord server listener.");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpGet("GetDiscordGuilds")]
        public IActionResult GetDiscordGuilds([FromServices] IDiscordRegistry reg)
        {
            try
            {

                return Ok(reg.GetAllRegisteredGuilds());
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error retrieving Discord guilds.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpGet("GetRolesByGuildId")]
        public async Task<IActionResult> GetRolesByGuildId([FromQuery] ulong guildId)
        {
            try
            {
                var roles = await _eventManager.GetGuildRoles(guildId);
                if (roles == null || !roles.Any())
                {
                    return NotFound($"No roles found for guild ID {guildId}.");
                }
                return Ok(roles);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error retrieving roles for guild ID {GuildId}.", guildId);
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

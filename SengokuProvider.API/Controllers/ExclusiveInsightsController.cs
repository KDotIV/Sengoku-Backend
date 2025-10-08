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
        private readonly ICustomerReportService _reportService;
        public ExclusiveInsightsController(ILogger<ExclusiveInsightsController> logger, EventListenerManager manager, ICustomerReportService reportService, CommandProcessor processor)
        {
            _log = logger;
            _commandProcessor = processor;
            _eventManager = manager;
            _reportService = reportService;
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
        [HttpGet("GetCustomersReportByGuildId")]
        public async Task<IActionResult> GetCustomersDataByGuildId(
            [FromQuery] ulong guildId,
            [FromQuery] string? email = null,
            [FromQuery] bool toSheets = false,
            [FromQuery] bool saveLocal = false,
            [FromQuery] ReportFormat format = ReportFormat.Excel)
        {
            try
            {
                var opts = new GenerateReportOptions
                {
                    EmailTo = email,
                    UploadToGoogleSheets = toSheets,
                    SaveToLocal = saveLocal,
                    Format = format,
                    SheetTitle = $"Guild-{guildId}-Report"
                };

                var (file, contentType, fileName, googleUrl) =
                    await _reportService.GenerateCustomerReportAsync(guildId, opts, HttpContext.RequestAborted);

                return Ok(new
                {
                    emailed = !string.IsNullOrWhiteSpace(email),
                    savedLocal = saveLocal,
                    googleUrl,
                    fileName,
                    directory = saveLocal ? Path.Combine(AppContext.BaseDirectory, "reports") : null
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error generating report for guild ID {GuildId}.", guildId);
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }
    }
}

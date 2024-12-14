using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Orgs;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/events/")]
    public class EventController : Controller
    {
        private readonly ILogger<EventController> _log;
        private readonly IEventIntakeService _eventIntakeService;
        private readonly IEventQueryService _eventQueryService;
        private readonly IOrganizerQueryService _organizerQueryService;
        private readonly CommandProcessor _commandProcessor;

        public EventController(ILogger<EventController> logger, IEventIntakeService eventIntakeService, IEventQueryService eventQueryService,
            IOrganizerQueryService orgQueryService, CommandProcessor command)
        {
            _log = logger;
            _eventIntakeService = eventIntakeService;
            _eventQueryService = eventQueryService;
            _commandProcessor = command;
            _organizerQueryService = orgQueryService;
        }

        [HttpPost("IntakeEventsByLocation")]
        public async Task<IActionResult> IntakeTournamentsByLocation([FromBody] IntakeEventsByLocationCommand command)
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
                var result = await _eventIntakeService.SendEventIntakeLocationMessage(command);
                return new OkObjectResult($"Intake Message Successfully Sent");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpPost("IntakeNewRegion")]
        public async Task<IActionResult> IntakeNewRegion([FromBody] IntakeNewRegionByIdCommand command)
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

            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking new Region Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
            return Ok();
        }
        [HttpPost("IntakeTournamentsByGameIds")]
        public async Task<IActionResult> IntakeTournamentsByGameIds([FromBody] IntakeEventsByGameIdCommand command)
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
                var result = await _eventIntakeService.IntakeEventsByGameId(command);
                return new OkObjectResult($"Tournaments Inserted: {result}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("LinkTournamentByEventIdCommand")]
        public async Task<IActionResult> LinkTournamentByEventIdCommand([FromBody] LinkTournamentByEventIdCommand command)
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
                var result = await _eventIntakeService.SendTournamentLinkEventMessage(command.EventLinkId);
                return new OkObjectResult($"Tournaments Inserted: {result}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpGet("QueryEventsByLocation")]
        public async Task<IActionResult> QueryEventsByLocation(
            [FromQuery] string regionId,
            [FromQuery] int[] gameIds,
            [FromQuery] int perPage = 50,
            [FromQuery] string priority = "date")
        {
            if (string.IsNullOrEmpty(regionId))
            {
                _log.LogError("Invalid RegionId parameter");
                return BadRequest("RegionId must be a positive integer.");
            }
            var command = new GetTournamentsByLocationCommand
            {
                RegionId = regionId,
                PerPage = perPage,
                Priority = priority,
                GameIds = gameIds ?? new int[0]
            };

            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return BadRequest(parsedRequest.Response);
            }

            try
            {
                var result = await _eventQueryService.GetEventsByLocation(parsedRequest);
                if (result.Count == 0) { return Ok("There are no tournaments under that region id."); }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Tournament Data.");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error message: {ex.Message}");
            }
        }
        [HttpGet("QueryRelatedRegionsById")]
        public async Task<IActionResult> QueryRelatedRegionsById([FromQuery] string regionId)
        {
            if (string.IsNullOrEmpty(regionId))
            {
                _log.LogError("Invalid RegionId parameter");
                return BadRequest("RegionId must be a positive integer.");
            }

            try
            {
                var result = await _eventQueryService.QueryRelatedRegionsById(regionId);
                if (result.Count == 0)
                {
                    return Ok("There are no related regions under that region id.");
                }

                return Ok(new { RelatedRegionsCount = result.Count, Regions = result });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Regional Data.");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error message: {ex.Message}");
            }
        }
        [HttpGet("GetCurrentBracketQueue")]
        public async Task<IActionResult> GetCurrentBracketQueueByTournamentId([FromQuery] int tournamentId)
        {
            if (tournamentId <= 0)
            {
                _log.LogError("Invalid TournamentId parameter");
                return BadRequest("TournamentId must be a positive integer.");
            }

            try
            {
                var result = await _organizerQueryService.GetBracketQueueByTournamentId(tournamentId);

                if (result == null || !result.Any())
                {
                    return Ok("There are no entries in the current bracket queue for that tournament ID.");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Bracket Queue Data.");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error message: {ex.Message}");
            }
        }
    }
}
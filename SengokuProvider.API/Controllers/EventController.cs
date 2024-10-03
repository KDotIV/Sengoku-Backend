using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/events/")]
    public class EventController : Controller
    {
        private readonly ILogger<EventController> _log;
        private readonly IEventIntakeService _eventIntakeService;
        private readonly IEventQueryService _eventQueryService;
        private readonly CommandProcessor _commandProcessor;

        public EventController(ILogger<EventController> logger, IEventIntakeService eventIntakeService, IEventQueryService eventQueryService, CommandProcessor command)
        {
            _log = logger;
            _eventIntakeService = eventIntakeService;
            _eventQueryService = eventQueryService;
            _commandProcessor = command;
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
        [HttpPost("QueryEventsByLocation")]
        public async Task<IActionResult> QueryEventsByLocation([FromBody] GetTournamentsByLocationCommand command)
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
                var result = await _eventQueryService.GetEventsByLocation(parsedRequest);
                if (result.Count == 0) { return new OkObjectResult($"There are no tournaments under that region id"); }
                var resultJson = JsonConvert.SerializeObject(result);
                return new OkObjectResult($"{resultJson}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
        [HttpPost("QueryRelatedRegionsById")]
        public async Task<IActionResult> QueryRelatedRegionsById([FromBody] GetRelatedRegionsCommand command)
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
                var result = await _eventQueryService.QueryRelatedRegionsById(command.RegionId);
                if (result.Count == 0) { return new OkObjectResult($"There are no related regions under that region id"); }
                var resultJson = JsonConvert.SerializeObject(result);
                return new OkObjectResult($"( Related Regions: {result.Count}) {resultJson}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Regional Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpPost("GetCurrentBracketQueue")]
        public async Task<IActionResult> GetCurrentBracketQueueByTournamentId([FromBody] GetCurrentBracketQueueByTournamentCommand command)
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

                return new OkObjectResult($"( Related Regions: )");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Querying Regional Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
    }
}
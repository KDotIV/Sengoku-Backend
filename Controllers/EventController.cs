using Microsoft.AspNetCore.Mvc;
using SengokuProvider.API.Models.Events;
using SengokuProvider.API.Services.Common;
using SengokuProvider.API.Services.Events;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/events/")]
    public class EventController : Controller
    {
        private readonly ILogger<EventController> _log;
        private readonly IEventService _eventService;
        private readonly CommandProcessor _commandProcessor;

        public EventController(ILogger<EventController> logger, IEventService eventService, CommandProcessor command)
        {
            _log = logger;
            _eventService = eventService;
            _commandProcessor = command;
        }

        [HttpGet("IntakeTournaments")]
        public async Task<IActionResult> IntakeTournaments([FromBody] TournamentIntakeCommand command)
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
                var result = await _eventService.IntakeTournamentData(command);
                return new OkObjectResult($"Addresses Inserted: {result.Item1} - Events Inserted: {result.Item2}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Intaking Tournament Data.");
                return new ObjectResult($"Error message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
    }
}
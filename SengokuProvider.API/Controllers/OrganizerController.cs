using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.Orgs;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Orgs;

namespace SengokuProvider.API.Controllers
{
    [Route("api/orgs/")]
    public class OrganizerController : Controller
    {
        private readonly ILogger<OrganizerController> _log;
        private readonly IEventQueryService _eventQueryService;
        private readonly IOrganizerQueryService _orgQueryService;
        private readonly IOrganizerIntakeService _orgIntakeService;
        private readonly CommandProcessor _commandProcessor;

        public OrganizerController(ILogger<OrganizerController> logger, IEventQueryService eventQueryService,
            IOrganizerQueryService orgQueryService, IOrganizerIntakeService orgIntakeService, CommandProcessor commandProcessor)
        {
            _log = logger;
            _eventQueryService = eventQueryService;
            _orgQueryService = orgQueryService;
            _orgIntakeService = orgIntakeService;
            _commandProcessor = commandProcessor;
        }
        [HttpGet("GetCoOpResultsUser")]
        public async Task<IActionResult> GetCoOpResultsByUserId([FromQuery] int userId)
        {
            var result = await _orgQueryService.GetCoOpResultsByUserId(userId);
            if (result.Count == 0) { return Ok("There are no CoOps under this User"); }

            return Ok(result);
        }
        [HttpPost("CreateTravelCoOp")]
        public async Task<IActionResult> CreateTravelCoOp([FromBody] CreateTravelCoOpCommand command)
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
                bool result = await _orgIntakeService.CreateTravelCoOp(command);
                return new OkObjectResult($"Travel CoOp Successfully Created");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error Creating New CoOp");
                return new ObjectResult($"Error Message: {ex.Message} - {ex.StackTrace}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }
    }
}

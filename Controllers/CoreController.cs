using Microsoft.AspNetCore.Mvc;
using SengokuProvider.API.Models.Common;
using SengokuProvider.API.Services.Common;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/core/")]
    public class CoreController
    {
        private readonly ILogger<CoreController> _logger;
        private readonly ICommonDatabaseService _commonDb;

        public CoreController(ILogger<CoreController> logger, ICommonDatabaseService commonDb)
        {
            _logger = logger;
            _commonDb = commonDb;
        }

        [HttpPost("CreateTable")]
        public async Task<IActionResult> CreateDatabaseTable([FromBody] CreateTableCommand command)
        {
            var parsedRequest = await _commonDb.ParseRequest(command);

            if (parsedRequest.TableName == "BadRequest" || parsedRequest == null) return new BadRequestObjectResult(parsedRequest.Response);

            var result = await _commonDb.CreateTable(parsedRequest.TableName, parsedRequest.TableDefinitions);

            if (result == -1) { return new OkResult(); }
            return new ObjectResult("Request was not valid");
        }
    }
}

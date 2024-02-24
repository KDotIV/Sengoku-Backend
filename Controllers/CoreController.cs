﻿using Microsoft.AspNetCore.Mvc;
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
        public async Task<IActionResult> CreateDatabaseTable(HttpRequest req)
        {
            var parsedRequest = await _commonDb.ParseRequest(req);

            if (parsedRequest.TableName == "BadRequest" || parsedRequest == null) return new BadRequestObjectResult(parsedRequest.Response);

            var result = await _commonDb.CreateTable(parsedRequest.TableName, parsedRequest.TableDefinitions);

            if (result > 0) { return new OkResult(); }
            return new ObjectResult("Request was not valid");
        }
    }
}

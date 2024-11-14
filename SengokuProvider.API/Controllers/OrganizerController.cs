using Microsoft.AspNetCore.Mvc;
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


    }
}

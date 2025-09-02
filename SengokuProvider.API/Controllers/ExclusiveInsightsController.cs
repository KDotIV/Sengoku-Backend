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
        private readonly ICustomerIntakeService _customerIntakeService;
        private readonly ICustomerQueryService _customerQueryService;
        private readonly CommandProcessor _commandProcessor;
        public ExclusiveInsightsController(ILogger<ExclusiveInsightsController> logger,
            ICustomerIntakeService customerIntake, ICustomerQueryService customerQuery, CommandProcessor processor)
        {
            _log = logger;
            _commandProcessor = processor;
            _customerIntakeService = customerIntake;
            _customerQueryService = customerQuery;
        }

    }
}

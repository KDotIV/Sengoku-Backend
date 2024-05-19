using SengokuProvider.Library.Services.Events;
using SengokuProvider.Worker.Factories;

namespace SengokuProvider.Worker.Handlers
{
    public class DataIntegrityWorker : BackgroundService
    {
        private readonly ILogger<DataIntegrityWorker> _logger;
        private readonly IEventIntakeService _eventIntakeService;
        private readonly IEventQueryService _eventQueryService;
        private readonly IEventIntegrityService _eventIntegrityService;
        private readonly IEventIntegrityFactory _eventFactory;

        public DataIntegrityWorker(ILogger<DataIntegrityWorker> logger, IEventIntakeService eventsIntake, IEventQueryService eventsQuery,
            IEventIntegrityService eventIntegrity, IEventIntegrityFactory eventFactory)
        {
            _logger = logger;
            _eventIntakeService = eventsIntake;
            _eventQueryService = eventsQuery;
            _eventIntegrityService = eventIntegrity;
            _eventFactory = eventFactory;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }
                var _eventHandler = _eventFactory.CreateEventFactory();
                var linksToProcess = await _eventHandler.BeginIntegrityTournamentLinks();

                if (linksToProcess.Count == 0) { Console.WriteLine("No Links to Process"); return; }
                Console.WriteLine($"Links to Process: {linksToProcess.Count}");
                foreach (var link in linksToProcess)
                {
                    try
                    {
                        Console.WriteLine($"Attempting to Update Link {link}");

                        var result = await _eventIntakeService.IntakeEventsByTournamentId(link);
                        if (result)
                        {
                            var verify = await _eventHandler.VerifyTournamentLinkChange(link);

                            if (verify)
                            {
                                Console.WriteLine($"Tournament_Link: {link} updated");
                            }
                        }
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        throw new ApplicationException("Error During Processing: ", ex);
                    }
                }
            }
        }
    }
}

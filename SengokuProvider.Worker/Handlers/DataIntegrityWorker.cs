using SengokuProvider.Library.Models.Events;
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
                        Console.WriteLine($"Error while Processing TournamentLink {ex.Message} - {ex.StackTrace}");
                    }
                }

                var eventsToUpdate = await _eventHandler.BeginEventIntegrity();
                Console.WriteLine($"Events to Update: {eventsToUpdate.Count}");
                foreach (var currentEventLink in eventsToUpdate)
                {
                    try
                    {
                        Console.WriteLine($"Attempting to Update EventId: {currentEventLink}");

                        UpdateEventCommand? updatedEventCommand = await _eventHandler.CreateUpdateCommand(currentEventLink);
                        await Task.Delay(1000);
                        if (updatedEventCommand == null) { continue; }

                        var updatedEvent = await _eventIntakeService.UpdateEventData(updatedEventCommand);
                        if (updatedEvent) { Console.WriteLine($"Successfully Updated: {updatedEventCommand.EventId}"); }
                        else { Console.WriteLine("Failed to update Event"); }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while Processing EventData Integrity {ex.Message} - {ex.StackTrace}");
                    }
                }
            }
        }
    }
}

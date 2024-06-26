using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Worker.Factories;

namespace SengokuProvider.Worker.Handlers
{
    internal class EventReceivedWorker : BackgroundService
    {
        private readonly ILogger<EventReceivedWorker> _log;
        private readonly IEventHandlerFactory _eventFactory;
        private readonly IConfiguration _configuration;

        private ServiceBusClient _client;
        private ServiceBusProcessor? _processor;

        public EventReceivedWorker(ILogger<EventReceivedWorker> logger, IConfiguration config, ServiceBusClient client, IEventHandlerFactory eventFactory)
        {
            _log = logger;
            _configuration = config;
            _client = client;
            _eventFactory = eventFactory;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _processor = _client.CreateProcessor(_configuration["ServiceBusSettings:EventReceivedQueue"], new ServiceBusProcessorOptions { MaxConcurrentCalls = 5, PrefetchCount = 5, });
            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += Errorhandler;

            await _processor.StartProcessingAsync();

            await GroomEventData();
            return;
        }

        private async Task GroomEventData()
        {
            var _integrityHandler = _eventFactory.CreateIntegrityHandler();
            var _intakeHandler = _eventFactory.CreateIntakeHandler();

            var eventsToUpdate = await _integrityHandler.BeginEventIntegrity();
            Console.WriteLine($"Events to Update: {eventsToUpdate.Count}");
            foreach (var currentEventLink in eventsToUpdate)
            {
                try
                {
                    Console.WriteLine($"Attempting to Update EventId: {currentEventLink}");

                    UpdateEventCommand? updatedEventCommand = await _integrityHandler.CreateUpdateCommand(currentEventLink);
                    if (updatedEventCommand == null) { continue; }

                    var updatedEvent = await _intakeHandler.UpdateEventData(updatedEventCommand);
                    if (updatedEvent) { Console.WriteLine($"Successfully Updated: {updatedEventCommand.EventId}"); }
                    else { Console.WriteLine("Failed to update Event"); }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while Processing EventData Integrity {ex.Message} - {ex.StackTrace}");
                }
            }

            var linksToProcess = await _integrityHandler.BeginIntegrityTournamentLinks();
            if (linksToProcess.Count == 0) { Console.WriteLine("No Links to Process"); return; }
            Console.WriteLine($"Links to Process: {linksToProcess.Count}");
            foreach (var link in linksToProcess)
            {
                try
                {
                    Console.WriteLine($"Attempting to Update Link {link}");

                    var result = await _intakeHandler.SendTournamentIntakeEventMessage(link);
                    if (result)
                    {
                        var verify = await _integrityHandler.VerifyTournamentLinkChange(link);

                        if (verify)
                        {
                            Console.WriteLine($"Tournament_Link: {link} updated");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while Processing TournamentLink {ex.Message} - {ex.StackTrace}");
                }
            }
        }
        private Task Errorhandler(ProcessErrorEventArgs args)
        {
            _log.LogError($"Error Processing Message: {args.ErrorSource}: {args.FullyQualifiedNamespace} {args.EntityPath} {args.Exception}");
            return Task.CompletedTask;
        }
        private async Task MessageHandler(ProcessMessageEventArgs args)
        {
            _log.LogWarning("Received Message...");

            var currentMessage = await ParseMessage(args.Message);
            if (currentMessage == null) { return; }

            try
            {
                switch (currentMessage.Topic)
                {
                    case EventCommandRegistry.UpdateEvent:
                        await UpdateEvent(currentMessage);
                        break;
                    case EventCommandRegistry.IntakeEventsByLocation:
                        List<int> result = await IntakeLocationEvents(currentMessage);
                        Console.WriteLine($"Successfully Added: {result} Events");

                        break;
                }
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _log.LogError(ex.Message, ex);
                await args.DeadLetterMessageAsync(args.Message);
                throw;
            }
        }
        private async Task<List<int>> IntakeLocationEvents(EventReceivedData? currentMessage)
        {
            List<int> successList = new List<int>();
            if (currentMessage == null) { return successList; }
            if (currentMessage.Command is IntakeEventsByLocationCommand intakeCommand)
            {
                var currentIntakeHandler = _eventFactory.CreateIntakeHandler();
                successList = await currentIntakeHandler.IntakeTournamentData(intakeCommand);
                if (successList.Count <= 0) new ApplicationException($"Failed to Intake Tournament Batch");
                return successList;
            }
            else { throw new InvalidOperationException("Command is not of expected type IntakeLocationCommand"); }
        }
        private async Task UpdateEvent(EventReceivedData? currentMessage)
        {
            if (currentMessage == null) { return; }
            if (currentMessage.Command is UpdateEventCommand updateCommand)
            {
                var currentUpdateHandler = _eventFactory.CreateIntakeHandler();
                var result = await currentUpdateHandler.UpdateEventData(updateCommand);
                if (!result) new ApplicationException($"Failed to update Event Data");
            }
            else { throw new InvalidOperationException("Command is not of expected type UpdateEventCommand"); }
            _log.LogInformation("Successfully Updated Event");
        }
        private async Task<EventReceivedData?> ParseMessage(ServiceBusReceivedMessage message)
        {
            using Stream bodyStream = message.Body.ToStream();
            using var reader = new StreamReader(bodyStream);

            var data = await reader.ReadToEndAsync();

            try
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter> { new CommandSerializer() }
                };
                return JsonConvert.DeserializeObject<EventReceivedData>(data, settings);
            }
            catch (JsonException ex)
            {
                _log.LogError(ex.Message);
            }
            return null;
        }
    }
}

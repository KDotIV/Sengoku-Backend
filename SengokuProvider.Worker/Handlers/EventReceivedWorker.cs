using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Common;
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
            _processor = _client.CreateProcessor(_configuration["ServiceBusSettings:EventReceivedQueue"], new ServiceBusProcessorOptions { MaxConcurrentCalls = 1, PrefetchCount = 2, });
            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += Errorhandler;

            await _processor.StartProcessingAsync();

            //await GroomEventData();
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
        }
        private Task Errorhandler(ProcessErrorEventArgs args)
        {
            _log.LogError($"Error Processing Message: {args.ErrorSource}: {args.FullyQualifiedNamespace} {args.EntityPath} {args.Exception}");
            return Task.CompletedTask;
        }
        private async Task RenewMessageLockUntilComplete(ProcessMessageEventArgs args, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); // Renew lock every 30 seconds
                    await args.RenewMessageLockAsync(args.Message, cancellationToken);
                    Console.WriteLine("Message Lock was renewed");
                }
                catch (TaskCanceledException)
                {
                    // Task was cancelled, no action needed
                }
                catch (Exception ex)
                {
                    _log.LogError($"Error renewing message lock: {ex.Message}", ex);
                }
            }
        }
        private async Task MessageHandler(ProcessMessageEventArgs args)
        {
            _log.LogWarning("Received Message...");

            var currentMessage = await ParseMessage(args.Message);
            if (currentMessage == null) { return; }

            CancellationTokenSource cts = new CancellationTokenSource();
            var lockRenewalTask = RenewMessageLockUntilComplete(args, cts.Token);

            try
            {
                switch (currentMessage.Command.Topic)
                {
                    case CommandRegistry.UpdateEvent:
                        await UpdateEvent(currentMessage);
                        break;
                    case CommandRegistry.IntakeEventsByLocation:
                        List<int> locationResult = await IntakeLocationEvents(currentMessage);
                        Console.WriteLine($"Successfully Added: {locationResult.Count} Events");
                        break;
                    case CommandRegistry.LinkTournamentByEvent:
                        int tournamentResult = await IntakeEventByTournamentId(currentMessage);
                        if (tournamentResult == 0) { Console.WriteLine($"Failed to Intake Tournament"); }
                        Console.WriteLine($"Successfully Added Tournament Data");
                        break;
                }
                await args.CompleteMessageAsync(args.Message);
                cts.Cancel();
            }
            catch (Exception ex)
            {
                _log.LogError(ex.Message, ex);
                await args.DeadLetterMessageAsync(args.Message, ex.Message, ex.StackTrace.ToString());
                cts.Cancel();
                throw;
            }
        }

        private async Task<int> IntakeEventByTournamentId(EventReceivedData currentMessage)
        {
            if (currentMessage == null) { return 0; }
            if (currentMessage.Command is LinkTournamentByEventIdCommand intakeCommand)
            {
                var currentIntakeHandler = _eventFactory.CreateIntakeHandler();
                return await currentIntakeHandler.IntakeTournamentIdData(intakeCommand);
            }
            return 0;
        }

        private async Task<List<int>> IntakeLocationEvents(EventReceivedData? currentMessage)
        {
            List<int> successList = new List<int>();
            if (currentMessage == null) { return successList; }
            if (currentMessage.Command is IntakeEventsByLocationCommand intakeCommand)
            {
                var currentIntakeHandler = _eventFactory.CreateIntakeHandler();
                successList = await currentIntakeHandler.IntakeTournamentData(intakeCommand);
                if (successList.Count == 0) new ApplicationException($"Failed to Intake Tournament Batch");
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

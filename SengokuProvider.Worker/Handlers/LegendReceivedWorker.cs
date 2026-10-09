
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Worker.Factories;
using SengokuProvider.Library.Workflows.Legends;
using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.Worker.Handlers
{
    internal class LegendReceivedWorker : BackgroundService
    {
        private readonly ILogger<LegendReceivedWorker> _log;
        private readonly ILegendHandlerFactory _legendFactory;
        private readonly IPlayerHandlerFactory _playerFactory;
        private readonly ILegendsOperations _legendsOperations;
        private readonly IConfiguration _configuration;
        private readonly IBracketCheckpointStore _checkpoints;

        private ServiceBusClient _client;
        private ServiceBusProcessor? _processor;
        public LegendReceivedWorker(ILogger<LegendReceivedWorker> logger, IConfiguration config, ServiceBusClient serviceBus, ILegendHandlerFactory legendFactory, IPlayerHandlerFactory playerFactory, ILegendsOperations legendsOperations, IBracketCheckpointStore checkpoints)
        {
            _log = logger;
            _configuration = config;
            _client = serviceBus;
            _legendFactory = legendFactory;
            _playerFactory = playerFactory;
            _legendsOperations = legendsOperations;
            _checkpoints = checkpoints;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _processor = _client.CreateProcessor(_configuration["ServiceBusSettings:LegendReceivedQueue"], new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1, PrefetchCount = 2, });
            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += Errorhandler;

            await _processor.StartProcessingAsync();

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            //await GroomLegendData();
            return;
        }
        private async Task GroomLegendData()
        {
            var currentIntegrity = _legendFactory.CreateIntegrityHandler();

            var playersToProcess = await currentIntegrity.BeginLegendIntegrity();
            if (playersToProcess.Count == 0) { Console.WriteLine("No Legends to Update from Players..."); return; }

            foreach (var player in playersToProcess)
            {
                int result = await OnboardNewLegendByPlayerData(player);
                if (result == 0) { Console.WriteLine($"Failed to Onboard"); }
                else { Console.WriteLine($"Successfully Added: Legend ID: {result}"); }
            }

            Console.WriteLine("Groom Legend Data Operation Completed...");
        }

        private async Task<int> OnboardNewLegendByPlayerData(OnboardLegendsByPlayerCommand player)
        {
            throw new NotImplementedException();
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
            if (currentMessage == null) { throw new NullReferenceException(); }

            try
            {
                switch (currentMessage.Command.Topic)
                {
                    case CommandRegistry.UpdateLegend:
                        await UpdateLegend(currentMessage);
                        break;
                    case CommandRegistry.OnboardLegendsByPlayerData:
                        int result = await OnboardNewLegendByPlayerData(currentMessage);
                        if (result == 0) { Console.WriteLine($"Failed to Onboard"); }
                        else { Console.WriteLine($"Successfully Added: Legend ID: {result}"); }
                        break;
                    case CommandRegistry.OnboardTournamentToLeague:
                        TournamentOnboardResult response = await OnboardTournamentToLeague(currentMessage);
                        if (response.Successful.Count > 0)
                        {
                            Console.WriteLine($"Successfully Added Tournament to League: {response.Response}");
                        }
                        break;
                    case CommandRegistry.OnboardPlayersByLinkData:
                        int newPlayerLinkResponse = await OnboardNewPlayerLinks(currentMessage);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported legend command: {currentMessage.Command.Topic}");
                }
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _log.LogError(ex.Message, ex);
                if (currentMessage.Command is OnboardLegendsByPlayerLinkCommand &&
                    ex is not ArgumentException && ex is not NotSupportedException)
                    await args.AbandonMessageAsync(args.Message);
                else
                    await args.DeadLetterMessageAsync(args.Message, ex.Message, ex.StackTrace?.ToString());
                throw;
            }
        }

        private async Task<int> OnboardNewLegendByPlayerData(OnboardReceivedData currentMessage)
        {
            throw new NotImplementedException();
        }

        private async Task<TournamentOnboardResult> OnboardTournamentToLeague(OnboardReceivedData currentMessage)
        {
            if (currentMessage == null) { return new TournamentOnboardResult { Response = "Onboard ServiceBus Message cannot be null" }; }

            var currentIntake = _legendFactory.CreateIntakeHandler();
            if (currentMessage.Command is OnboardTournamentToLeagueCommand onboardCommand)
            {
                TournamentOnboardResult result = await currentIntake.AddTournamentToLeague(onboardCommand.TournamentIds, onboardCommand.LeagueId);

                return result;
            }
            return new TournamentOnboardResult { Response = "Unexpected Error Occured" };
        }
        private async Task<int> OnboardNewPlayerLinks(OnboardReceivedData currentMessage)
        {
            if (currentMessage == null) { return 0; }
            var currentPlayerQuery = _playerFactory.CreateQueryHandler();

            if (currentMessage.Command is OnboardLegendsByPlayerLinkCommand onboardCommand)
            {
                if (!onboardCommand.Validate()) throw new ArgumentException("Player links are required.");
                if (onboardCommand.OperationId is Guid operationId)
                {
                    var checkpoint = await _checkpoints.GetAsync(operationId);
                    if (checkpoint == null || checkpoint.Result.Status != "Pending" || checkpoint.ExpiresAt <= DateTime.UtcNow)
                        return 0;
                }
                var newLegends = await _legendsOperations.GenerateNewLegendsByPlayerLinks(onboardCommand.PlayerLinkIds);
                var inserted = newLegends.Count == 0 ? 0 : await _legendFactory.CreateIntakeHandler().InsertNewLegendData(newLegends);
                // A duplicate onboarding request still wakes the waiting operation.
                if (onboardCommand.OperationId is Guid resumeId)
                    await _checkpoints.EnqueueResumeAsync(resumeId, _configuration["ServiceBusSettings:PlayerReceivedQueue"]!);
                return inserted;
            }
            return 0;
        }
        private async Task UpdateLegend(OnboardReceivedData currentMessage)
        {
            throw new NotImplementedException();
        }

        private async Task<OnboardReceivedData?> ParseMessage(ServiceBusReceivedMessage message)
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
                return JsonConvert.DeserializeObject<OnboardReceivedData>(data, settings);
            }
            catch (JsonException ex)
            {
                _log.LogError(ex.Message);
            }
            return null;
        }
    }
}

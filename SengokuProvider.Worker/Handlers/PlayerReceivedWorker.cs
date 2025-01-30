using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Worker.Factories;

namespace SengokuProvider.Worker.Handlers
{
    internal class PlayerReceivedWorker : BackgroundService
    {
        private readonly ILogger<PlayerReceivedWorker> _log;
        private readonly IPlayerHandlerFactory _playerFactory;
        private readonly IConfiguration _configuration;

        private ServiceBusClient _client;
        private ServiceBusProcessor _processor;

        public PlayerReceivedWorker(ILogger<PlayerReceivedWorker> logger, IConfiguration config, ServiceBusClient serviceBus,
            IPlayerHandlerFactory playerFactory)
        {
            _log = logger;
            _playerFactory = playerFactory;
            _configuration = config;
            _client = serviceBus;
            _processor = _client.CreateProcessor(_configuration["ServiceBusSettings:PlayerReceivedQueue"], new ServiceBusProcessorOptions { MaxConcurrentCalls = 1, PrefetchCount = 2, });
            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += Errorhandler;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _processor.StartProcessingAsync(stoppingToken);

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            //await GroomLegendData();
            return;
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
            if (currentMessage == null) { throw new NullReferenceException(); }
            CancellationTokenSource cts = new CancellationTokenSource();
            var lockRenewalTask = RenewMessageLockUntilComplete(args, cts.Token);

            try
            {
                switch (currentMessage.Command.Topic)
                {
                    case CommandRegistry.UpdatePlayer:
                        break;
                    case CommandRegistry.OnboardPlayerData:
                        int onboardResult = await OnboardNewPlayer(currentMessage);
                        Console.WriteLine($"{onboardResult}");
                        break;
                    case CommandRegistry.IntakePlayersByTournament:
                        int result = await IntakeNewPlayers(currentMessage);
                        Console.WriteLine($"{result}");
                        break;
                    case CommandRegistry.QueryPlayerStandingsCommand:
                        List<PlayerStandingResult> playerResults = await QueryPlayerStandings(currentMessage);
                        break;
                }
                await args.CompleteMessageAsync(args.Message);
                cts.Cancel();
            }
            catch (Exception ex)
            {
                _log.LogError(ex.Message, ex);
                await args.DeadLetterMessageAsync(args.Message, ex.Message, ex.StackTrace?.ToString());
                cts.Cancel();
                throw;
            }
        }

        private async Task<List<PlayerStandingResult>> QueryPlayerStandings(PlayerReceivedData currentMessage)
        {
            List<PlayerStandingResult> result = new List<PlayerStandingResult>();
            if (currentMessage == null) { return result; }
            var currentQuery = _playerFactory.CreateQueryHandler();
            if (currentMessage.Command is GetPlayerStandingsCommand command)
            {
                result = await currentQuery.GetPlayerStandingResults(command);
            }
            return result;
        }

        private async Task<int> OnboardNewPlayer(PlayerReceivedData currentMessage)
        {
            if (currentMessage == null) { return 0; }
            var currentIntake = _playerFactory.CreateIntakeHandler();
            if (currentMessage.Command is OnboardPlayerDataCommand command)
            {
                return await currentIntake.OnboardPreviousTournamentData(command);
            }
            return 0;
        }
        private async Task<int> IntakeNewPlayers(PlayerReceivedData currentMessage)
        {
            if (currentMessage == null) { return 0; }

            var currentIntake = _playerFactory.CreateIntakeHandler();
            if (currentMessage.Command is IntakePlayersByTournamentCommand intakeCommand)
            {
                var successfulPlayers = await currentIntake.IntakePlayerData(intakeCommand);

                return successfulPlayers;
            }
            return 0;
        }

        private async Task<PlayerReceivedData?> ParseMessage(ServiceBusReceivedMessage message)
        {
            using Stream bodyStream = message.Body.ToStream();
            using var reader = new StreamReader(bodyStream);

            var data = await reader.ReadToEndAsync();

            try
            {
                Console.WriteLine(data);
                var settings = new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter> { new CommandSerializer() }
                };
                return JsonConvert.DeserializeObject<PlayerReceivedData>(data, settings);
            }
            catch (JsonException ex)
            {
                _log.LogError(ex.Message);
            }
            return null;
        }
    }
}

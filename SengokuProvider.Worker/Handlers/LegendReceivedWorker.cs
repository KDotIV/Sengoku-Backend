
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Worker.Factories;

namespace SengokuProvider.Worker.Handlers
{
    internal class LegendReceivedWorker : BackgroundService
    {
        private readonly ILogger<LegendReceivedWorker> _log;
        private readonly ILegendHandlerFactory _legendFactory;
        private readonly IConfiguration _configuration;

        private ServiceBusClient _client;
        private ServiceBusProcessor? _processor;
        public LegendReceivedWorker(ILogger<LegendReceivedWorker> logger, IConfiguration config, ServiceBusClient serviceBus, ILegendHandlerFactory legendFactory)
        {
            _log = logger;
            _configuration = config;
            _client = serviceBus;
            _legendFactory = legendFactory;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _processor = _client.CreateProcessor(_configuration["ServiceBusSettings:LegendReceivedQueue"], new ServiceBusProcessorOptions { MaxConcurrentCalls = 1, PrefetchCount = 2, });
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
                int result = await OnboardNewPlayer(player);
                if (result == 0) { Console.WriteLine($"Failed to Onboard"); }
                else { Console.WriteLine($"Successfully Added: Legend ID: {result}"); }
            }

            Console.WriteLine("Groom Legend Data Operation Completed...");
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
                        int result = await OnboardNewPlayer(currentMessage);
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
                }
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _log.LogError(ex.Message, ex);
                await args.DeadLetterMessageAsync(args.Message, ex.Message, ex.StackTrace?.ToString());
                throw;
            }
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

        private async Task<int> OnboardNewPlayer(OnboardReceivedData currentMessage)
        {
            if (currentMessage == null) { return 0; }

            var currentIntake = _legendFactory.CreateIntakeHandler();
            if (currentMessage.Command is OnboardLegendsByPlayerCommand onboardCommand)
            {
                var newLegend = await currentIntake.GenerateNewLegends(onboardCommand.PlayerId, onboardCommand.GamerTag);

                if (newLegend == null) { return 0; }

                int newLegendID = await currentIntake.InsertNewLegendData(newLegend);
                if (newLegendID > 0) { return newLegendID; }
            }
            return 0;
        }
        private async Task<int> OnboardNewPlayer(OnboardLegendsByPlayerCommand onboardCommand)
        {
            if (onboardCommand == null) { return 0; }

            var currentIntake = _legendFactory.CreateIntakeHandler();

            var newLegend = await currentIntake.GenerateNewLegends(onboardCommand.PlayerId, onboardCommand.GamerTag);

            if (newLegend == null) { return 0; }

            int newLegendID = await currentIntake.InsertNewLegendData(newLegend);
            if (newLegendID > 0) { return newLegendID; }
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

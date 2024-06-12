
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
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
            _processor = _client.CreateProcessor(_configuration["ServiceBusSettings:LegendReceivedQueue"], new ServiceBusProcessorOptions { MaxConcurrentCalls = 5, PrefetchCount = 5, });
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
                switch (currentMessage.Topic)
                {
                    case LegendCommandRegistry.UpdateLegend:
                        await UpdateLegend(currentMessage);
                        break;
                    case LegendCommandRegistry.OnboardLegendsByPlayerData:
                        int result = await OnboardNewPlayer(currentMessage);
                        if (result == 0) { Console.WriteLine($"Failed to Onboard PlayerId: {result}"); }
                        Console.WriteLine($"Successfully Added: Legends for PlayerID: {result}");

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

        private async Task<int> OnboardNewPlayer(OnboardReceivedData currentMessage)
        {
            if (currentMessage == null) { return 0; }

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

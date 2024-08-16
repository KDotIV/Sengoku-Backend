using Azure.Messaging.ServiceBus;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Worker.Factories;
using SengokuProvider.Worker.Handlers;
using System.Net.Http.Headers;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        services.AddHostedService<EventReceivedWorker>();
        services.AddHostedService<LegendReceivedWorker>();
        services.AddHostedService<PlayerReceivedWorker>();

        var connectionString = configuration.GetConnectionString("AlexandriaConnectionString");
        var graphQLUrl = configuration["GraphQLSettings:Endpoint"];
        var bearerToken = configuration["GraphQLSettings:Bearer"];
        var serviceBusConnection = configuration["ServiceBusSettings:AzureWebJobsServiceBus"];

        services.AddSingleton<IAzureBusApiService, AzureBusApiService>(provider =>
        {
            var client = provider.GetService<ServiceBusClient>();
            return new AzureBusApiService(client);
        });

        services.AddSingleton<IEventHandlerFactory, EventHandlerFactory>();
        services.AddSingleton<ILegendHandlerFactory, LegendHandlerFactory>();
        services.AddSingleton<IPlayerHandlerFactory, PlayerHandlerFactory>();
        services.AddSingleton(provider => { return new ServiceBusClient(serviceBusConnection); });
        services.AddSingleton(provider => new GraphQLHttpClient(graphQLUrl, new NewtonsoftJsonSerializer())
        {
            HttpClient = { DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", bearerToken) } }
        });
        services.AddSingleton(provider => { var config = provider.GetService<IConfiguration>(); return new RequestThrottler(config); });
        services.AddSingleton<ICommonDatabaseService, CommonDatabaseService>(provider =>
        {
            return new CommonDatabaseService(connectionString);
        });
        services.AddSingleton<IUserService, UserService>(provider =>
        {
            var intakeValidator = provider.GetRequiredService<IntakeValidator>();
            return new UserService(connectionString, intakeValidator);
        });
        services.AddSingleton<IEventIntakeService, EventIntakeService>(provider =>
        {
            var intakeValidator = provider.GetService<IntakeValidator>();
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var queryService = provider.GetService<IEventQueryService>();
            var throttler = provider.GetService<RequestThrottler>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            return new EventIntakeService(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler);
        });
        services.AddSingleton<IEventQueryService, EventQueryService>(provider =>
        {
            var intakeValidator = provider.GetService<IntakeValidator>();
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler);
        });
        services.AddSingleton<IEventIntegrityService, EventIntegrityService>(provider =>
        {
            var queryService = provider.GetService<IEventQueryService>();
            var intakeService = provider.GetService<IEventIntakeService>();
            return new EventIntegrityService(queryService, intakeService, connectionString);
        });
        services.AddSingleton<ILegendQueryService, LegendQueryService>(provider =>
        {
            var client = provider.GetService<GraphQLHttpClient>();
            return new LegendQueryService(connectionString, client);
        });
        services.AddSingleton<ILegendIntakeService, LegendIntakeService>(provider =>
        {
            var queryService = provider.GetService<ILegendQueryService>();
            var config = provider.GetService<IConfiguration>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            return new LegendIntakeService(connectionString, config, queryService, serviceBus);
        });
        services.AddSingleton<ILegendIntegrityService, LegendIntegrityService>(provider =>
        {
            var queryService = provider.GetService<ILegendQueryService>();
            var intakeService = provider.GetService<ILegendIntakeService>();
            return new LegendIntegrityService(connectionString, queryService, intakeService);
        });
        services.AddSingleton<IPlayerIntakeService, PlayerIntakeService>(provider =>
        {
            var playerQueryService = provider.GetService<IPlayerQueryService>();
            var legendQueryService = provider.GetService<ILegendQueryService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            return new PlayerIntakeService(connectionString, configuration, playerQueryService, legendQueryService, eventQueryService, serviceBus);

        });
        services.AddSingleton<IPlayerQueryService, PlayerQueryService>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var graphClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            return new PlayerQueryService(connectionString, configuration, graphClient, throttler);
        });
    })
    .Build();
await host.RunAsync();

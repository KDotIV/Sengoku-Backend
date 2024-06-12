using Azure.Messaging.ServiceBus;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
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

        var connectionString = configuration.GetConnectionString("AlexandriaConnectionString");
        var graphQLUrl = configuration["GraphQLSettings:Endpoint"];
        var bearerToken = configuration["GraphQLSettings:Bearer"];
        var serviceBusConnection = configuration["ServiceBusSettings:AzureWebJobsServiceBus"];

        // Add services to the container.
        services.AddSingleton<IEventHandlerFactory, EventHandlerFactory>();
        services.AddSingleton<ILegendHandlerFactory, LegendHandlerFactory>();
        services.AddSingleton(provider => { return new ServiceBusClient(serviceBusConnection); });
        services.AddSingleton(provider => new GraphQLHttpClient(graphQLUrl, new NewtonsoftJsonSerializer())
        {
            HttpClient = { DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", bearerToken) } }
        });
        services.AddSingleton<RequestThrottler>();
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
            return new EventIntakeService(connectionString, graphQlClient, queryService, intakeValidator, throttler);
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
    })
    .Build();
await host.RunAsync();

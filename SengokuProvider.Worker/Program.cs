using Azure.Messaging.ServiceBus;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Orgs;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Worker.Factories;
using SengokuProvider.Worker.Handlers;
using System.Net.Http.Headers;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((Action<HostBuilderContext, IServiceCollection>)((context, services) =>
    {
        var configuration = context.Configuration;
        services.AddHostedService<EventReceivedWorker>();
        services.AddHostedService<LegendReceivedWorker>();
        services.AddHostedService<PlayerReceivedWorker>();
        string connectionString, graphQLUrl, bearerToken, serviceBusConnection;

        SetupServiceDependencies(services, configuration, out connectionString, out graphQLUrl, out bearerToken, out serviceBusConnection);

        services.AddSingleton<IEventHandlerFactory, EventHandlerFactory>();
        services.AddSingleton<ILegendHandlerFactory, LegendHandlerFactory>();
        services.AddSingleton<IPlayerHandlerFactory, PlayerHandlerFactory>();

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
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler, commonServices);
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
            var commonService = provider.GetService<ICommonDatabaseService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            return new LegendQueryService(connectionString, client, commonService, eventQueryService);
        });
        services.AddSingleton<ILegendIntakeService, LegendIntakeService>(provider =>
        {
            var legendQueryService = provider.GetService<ILegendQueryService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            var userQueryService = provider.GetService<IUserService>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            var config = provider.GetService<IConfiguration>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            return new LegendIntakeService(connectionString, config, legendQueryService, eventQueryService, userQueryService, serviceBus, commonServices);
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
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new PlayerQueryService(connectionString, configuration, graphClient, throttler, commonServices);
        });
        services.AddSingleton<IOrganizerIntakeService, OrganizerIntakeService>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var graphClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            var userService = provider.GetService<IUserService>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new OrganizerIntakeService(connectionString, graphClient, throttler, userService, commonServices);
        });
    }))
    .Build();
await host.RunAsync();

static void SetupServiceDependencies(IServiceCollection services, IConfiguration configuration, out string connectionString, out string graphQLUrl, out string bearerToken, out string serviceBusConnection)
{
    connectionString = configuration.GetConnectionString("AlexandriaConnectionString") ?? throw new ArgumentNullException("Connection String is Null or Empty");
    graphQLUrl = configuration["GraphQLSettings:Endpoint"] ?? throw new ArgumentNullException("GraphQL Endpoint is Null or Empty");
    bearerToken = configuration["GraphQLSettings:Bearer"] ?? throw new ArgumentNullException("Bearer Token is Null or Empty");
    serviceBusConnection = configuration["ServiceBusSettings:AzureWebJobsServiceBus"] ?? throw new ArgumentNullException("Service Bus Connection is Null or Empty");

    var serviceBusClient = new ServiceBusClient(serviceBusConnection);

    var graphQLUrlCopy = graphQLUrl;
    var bearerTokenCopy = bearerToken;

    services.AddSingleton<IAzureBusApiService, AzureBusApiService>(provider =>
    {
        return new AzureBusApiService(serviceBusClient);
    });
    services.AddSingleton(provider => serviceBusClient);
    services.AddSingleton(provider => new GraphQLHttpClient(graphQLUrlCopy, new NewtonsoftJsonSerializer())
    {
        HttpClient = { DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", bearerTokenCopy) } }
    });
    services.AddSingleton(provider => { var config = provider.GetService<IConfiguration>(); return new RequestThrottler(config); });
}

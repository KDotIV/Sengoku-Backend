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

        services.AddSingleton(new IntakeValidator());
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
            var intakeValidator = provider.GetRequiredService<IntakeValidator>();
            var graphQlClient = provider.GetRequiredService<GraphQLHttpClient>();
            var queryService = provider.GetRequiredService<IEventQueryService>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var serviceBus = provider.GetRequiredService<IAzureBusApiService>();
            return new EventIntakeService(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler);
        });
        services.AddSingleton<IEventQueryService, EventQueryService>(provider =>
        {
            services.AddSingleton<IEventIntakeService, EventIntakeService>(provider =>
            {
                var intakeValidator = GetRequiredService<IntakeValidator>(provider);
                var graphQlClient = GetRequiredService<GraphQLHttpClient>(provider);
                var queryService = GetRequiredService<IEventQueryService>(provider);
                var throttler = GetRequiredService<RequestThrottler>(provider);
                var serviceBus = GetRequiredService<IAzureBusApiService>(provider);
                return new EventIntakeService(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler);
            });

            services.AddSingleton<IEventQueryService, EventQueryService>(provider =>
            {
                var intakeValidator = GetRequiredService<IntakeValidator>(provider);
                var graphQlClient = GetRequiredService<GraphQLHttpClient>(provider);
                var throttler = GetRequiredService<RequestThrottler>(provider);
                var commonServices = GetRequiredService<ICommonDatabaseService>(provider);
                return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler, commonServices);
            });

            services.AddSingleton<ILegendQueryService, LegendQueryService>(provider =>
            {
                var client = GetRequiredService<GraphQLHttpClient>(provider);
                var commonService = GetRequiredService<ICommonDatabaseService>(provider);
                var eventQueryService = GetRequiredService<IEventQueryService>(provider);
                return new LegendQueryService(connectionString, client, commonService, eventQueryService);
            });

            services.AddSingleton<ILegendIntakeService, LegendIntakeService>(provider =>
            {
                var legendQueryService = GetRequiredService<ILegendQueryService>(provider);
                var eventQueryService = GetRequiredService<IEventQueryService>(provider);
                var userQueryService = GetRequiredService<IUserService>(provider);
                var commonServices = GetRequiredService<ICommonDatabaseService>(provider);
                var config = GetRequiredService<IConfiguration>(provider);
                var serviceBus = GetRequiredService<IAzureBusApiService>(provider);
                return new LegendIntakeService(connectionString, config, legendQueryService, eventQueryService, userQueryService, serviceBus, commonServices);
            });
            var intakeValidator = provider.GetRequiredService<IntakeValidator>();
            var graphQlClient = provider.GetRequiredService<GraphQLHttpClient>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler, commonServices);
        });
        services.AddSingleton<IEventIntegrityService, EventIntegrityService>(provider =>
        {
            var queryService = provider.GetRequiredService<IEventQueryService>();
            var intakeService = provider.GetRequiredService<IEventIntakeService>();
            return new EventIntegrityService(queryService, intakeService, connectionString);
        });
        services.AddSingleton<ILegendQueryService, LegendQueryService>(provider =>
        {
            var client = provider.GetRequiredService<GraphQLHttpClient>();
            var commonService = provider.GetRequiredService<ICommonDatabaseService>();
            var eventQueryService = provider.GetRequiredService<IEventQueryService>();
            return new LegendQueryService(connectionString, client, commonService, eventQueryService);
        });
        services.AddSingleton<ILegendIntakeService, LegendIntakeService>(provider =>
        {
            var legendQueryService = provider.GetRequiredService<ILegendQueryService>();
            var eventQueryService = provider.GetRequiredService<IEventQueryService>();
            var userQueryService = provider.GetRequiredService<IUserService>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            var config = provider.GetRequiredService<IConfiguration>();
            var serviceBus = provider.GetRequiredService<IAzureBusApiService>();
            return new LegendIntakeService(connectionString, config, legendQueryService, eventQueryService, userQueryService, serviceBus, commonServices);
        });
        services.AddSingleton<ILegendIntegrityService, LegendIntegrityService>(provider =>
        {
            var queryService = provider.GetRequiredService<ILegendQueryService>();
            var intakeService = provider.GetRequiredService<ILegendIntakeService>();
            return new LegendIntegrityService(connectionString, queryService, intakeService);
        });
        services.AddSingleton<IPlayerIntakeService, PlayerIntakeService>(provider =>
        {
            var playerQueryService = provider.GetRequiredService<IPlayerQueryService>();
            var legendQueryService = provider.GetRequiredService<ILegendQueryService>();
            var eventQueryService = provider.GetRequiredService<IEventQueryService>();
            var serviceBus = provider.GetRequiredService<IAzureBusApiService>();
            return new PlayerIntakeService(connectionString, configuration, playerQueryService, legendQueryService, eventQueryService, serviceBus);

        });
        services.AddSingleton<IPlayerQueryService, PlayerQueryService>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var graphClient = provider.GetRequiredService<GraphQLHttpClient>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            return new PlayerQueryService(connectionString, configuration, graphClient, throttler, commonServices);
        });
        services.AddSingleton<IOrganizerIntakeService, OrganizerIntakeService>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var graphClient = provider.GetRequiredService<GraphQLHttpClient>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var userService = provider.GetRequiredService<IUserService>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            return new OrganizerIntakeService(connectionString, graphClient, throttler, userService, commonServices);
        });
    }))
    .Build();
await host.RunAsync();

static T GetRequiredService<T>(IServiceProvider provider)
{
    return provider.GetService<T>() ?? throw new ArgumentNullException(nameof(T));
}
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
    services.AddSingleton(provider =>
    {
        var config = provider.GetService<IConfiguration>(); return new RequestThrottler(config); static T GetRequiredService<T>(IServiceProvider provider)
        {
            return provider.GetService<T>() ?? throw new ArgumentNullException(nameof(T));
        }
    });
}

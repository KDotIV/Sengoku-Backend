using Azure.Messaging.ServiceBus;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Comms;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Orgs;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Library.Workflows.Players;
using SengokuProvider.Library.Workflows.Events;
using SengokuProvider.Library.Workflows.Orgs;
using System.Net.Http.Headers;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((ctx, ConfigurationBuilder) =>
    {
        ConfigurationBuilder.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
        ConfigurationBuilder.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        IConfiguration config = ctx.Configuration;
        services.AddSingleton<IConfiguration>(config);

        string connectionString = config["AlexandriaConnectionString"];
        string graphQLUrl = config["Endpoint"];
        string bearerToken = config["Bearer"];
        string serviceBusConnection = config["AzureWebJobsServiceBus"];

        services.AddTransient<CommandProcessor>();
        services.AddSingleton<IntakeValidator>();
        services.AddSingleton<RequestThrottler>();
        services.AddSingleton(provider => { return new ServiceBusClient(serviceBusConnection); });

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddSingleton<HttpClient>();
        services.AddScoped<IAzureBusApiService, AzureBusApiService>(provider =>
        {
            var client = provider.GetService<ServiceBusClient>();
            return new AzureBusApiService(client);
        });
        services.AddScoped<ICommonDatabaseService, CommonDatabaseService>(provider =>
        {
            return new CommonDatabaseService(connectionString);
        });
        services.AddScoped<IUserService, UserService>(provider =>
        {
            var intakeValidator = provider.GetRequiredService<IntakeValidator>();

            return new UserService(connectionString, intakeValidator);
        });
        services.AddScoped<IDiscordWebhookHandler, DiscordWebhookHandler>(provider =>
        {
            return new DiscordWebhookHandler(connectionString);
        });
        services.AddScoped<IOrganizerQueryService, OrganizerQueryService>(provider =>
        {
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new OrganizerQueryService(connectionString, graphQlClient, throttler, commonServices);
        });
        services.AddScoped<IOrganizerIntakeService, OrganizerOperations>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var graphClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            var userService = provider.GetService<IUserService>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new OrganizerOperations(connectionString, graphClient, throttler, userService, commonServices);
        });
        services.AddScoped<IEventIntakeService>(_ => new EventIntakeService(connectionString));
        services.AddScoped<IEventOperations>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var intakeValidator = provider.GetService<IntakeValidator>();
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var queryService = provider.GetService<IEventQueryService>();
            var throttler = provider.GetService<RequestThrottler>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            return new EventOperations(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler, provider.GetRequiredService<IEventIntakeService>());
        });
        services.AddScoped<ILegendQueryService, LegendQueryService>(provider =>
        {
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var commonService = provider.GetService<ICommonDatabaseService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            return new LegendQueryService(connectionString, graphQlClient, commonService, eventQueryService);
        });
        services.AddScoped<IPlayerQueryService, PlayerQueryService>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new PlayerQueryService(connectionString, configuration, graphQlClient, throttler, commonServices);
        });
        services.AddScoped<IPlayerIntakeService>(provider => new PlayerIntakeService(connectionString,
            provider.GetRequiredService<ICommonDatabaseService>(), provider.GetRequiredService<IEventQueryService>(),
            provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IAzureBusApiService>()));
        services.AddScoped<IBracketCheckpointStore>(provider => new BracketCheckpointStore(connectionString));
        services.AddScoped<IPlayerOperations>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            var playerQueryService = provider.GetService<IPlayerQueryService>();
            var legendQueryService = provider.GetService<ILegendQueryService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            var playerIntakeService = provider.GetRequiredService<IPlayerIntakeService>();
            var bracketCheckpointStore = provider.GetRequiredService<IBracketCheckpointStore>();
            return new PlayerOperations(connectionString, configuration, commonServices, playerQueryService, legendQueryService, eventQueryService, serviceBus, playerIntakeService, bracketCheckpointStore);
        });
        services.AddScoped(provider => new GraphQLHttpClient(graphQLUrl, new NewtonsoftJsonSerializer())
        {
            HttpClient = { DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", bearerToken) } }
        });
        services.AddScoped<IEventQueryService, EventQueryService>(provider =>
        {
            var intakeValidator = provider.GetService<IntakeValidator>();
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler, commonServices);
        });
        services.AddScoped<ILegendQueryService, LegendQueryService>(provider =>
        {
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var commonService = provider.GetService<ICommonDatabaseService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            return new LegendQueryService(connectionString, graphQlClient, commonService, eventQueryService);
        });
        services.AddScoped<ILegendIntakeService, LegendIntakeService>(provider =>
        {
            var queryService = provider.GetService<ILegendQueryService>();
            return new LegendIntakeService(connectionString, queryService);
        });
    })
    .Build();

host.Run();

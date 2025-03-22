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
using System.Net.Http.Headers;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings:AlexandriaConnectionString");
        string graphQLUrl = Environment.GetEnvironmentVariable("ConnectionStrings:Endpoint");
        string bearerToken = Environment.GetEnvironmentVariable("Bearer");
        string serviceBusConnection = Environment.GetEnvironmentVariable("AzureWebJobsServiceBus");

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
            var playerQuery = provider.GetService<IPlayerQueryService>();
            return new UserService(connectionString, intakeValidator, playerQuery);
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
        services.AddScoped<IOrganizerIntakeService, OrganizerIntakeService>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var graphClient = provider.GetService<GraphQLHttpClient>();
            var throttler = provider.GetService<RequestThrottler>();
            var userService = provider.GetService<IUserService>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new OrganizerIntakeService(connectionString, graphClient, throttler, userService, commonServices);
        });
        services.AddScoped<IEventIntakeService, EventIntakeService>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var intakeValidator = provider.GetService<IntakeValidator>();
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            var queryService = provider.GetService<IEventQueryService>();
            var throttler = provider.GetService<RequestThrottler>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            return new EventIntakeService(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler);
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
        services.AddScoped<IPlayerIntakeService, PlayerIntakeService>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var playerQueryService = provider.GetService<IPlayerQueryService>();
            var legendQueryService = provider.GetService<ILegendQueryService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            return new PlayerIntakeService(connectionString, configuration, playerQueryService, legendQueryService, eventQueryService, serviceBus);
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
            var config = provider.GetService<IConfiguration>();
            var serviceBus = provider.GetService<IAzureBusApiService>();
            var eventQueryService = provider.GetService<IEventQueryService>();
            var eventIntakeService = provider.GetService<IEventIntakeService>();
            var userQueryService = provider.GetService<IUserService>();
            var playerQueryService = provider.GetService<IPlayerQueryService>();
            var commonServices = provider.GetService<ICommonDatabaseService>();
            return new LegendIntakeService(connectionString, config, queryService, eventQueryService, eventIntakeService, userQueryService, playerQueryService, serviceBus, commonServices);
        });
    })
    .Build();

host.Run();

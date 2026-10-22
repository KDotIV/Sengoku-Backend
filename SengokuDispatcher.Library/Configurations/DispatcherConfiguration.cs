using SengokuDispatcher.Library.Services.Common;
using SengokuDispatcher.Library.Services.Common.Interfaces;
using SengokuDispatcher.Library.Services.Players;
using SengokuDispatcher.Library.Handlers;
using SengokuProvider.Library.Configurations;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Library.Workflows.Users;
using Azure.Messaging.ServiceBus;
using SengokuDispatcher.Library.DiscordServices;
using Google.Apis.Sheets.v4;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Npgsql;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuDispatcher.Library.Services.Comms;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Orgs;
using SengokuProvider.Library.Services.Players;
using SengokuDispatcher.Library.Workflows.Legends;
using SengokuDispatcher.Library.Workflows.Events;
using SengokuDispatcher.Library.Workflows.Players;
using SengokuProvider.Library.Workflows.Orgs;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Builder;
using SengokuDispatcher.Library.Factories;
using SengokuDispatcher.Library.Services.Scheduling;
using SengokuDispatcher.Library.Services.Events;
namespace SengokuDispatcher.Library.Configurations;

public static class DispatcherConfiguration
{
    public static void AddDispatcher(this WebApplicationBuilder builder, bool backgroundHost = true)
    {
        //constant config variables
        var connectionString = builder.Configuration.GetConnectionString("AlexandriaConnectionString")
            ?? throw new InvalidOperationException("Alexandria database connection is required.");
        var graphQLUrl = builder.Configuration["GraphQLSettings:Endpoint"] ?? "https://api.start.gg/gql/alpha";
        var bearerToken = builder.Configuration["GraphQLSettings:Bearer:0"]
            ?? builder.Configuration["GraphQLSettings:Bearer"];
        var serviceBusConnection = builder.Configuration["ServiceBusSettings:AzureWebJobsServiceBus"];
        var customerPoolConnection = builder.Configuration["ExclusiveInsightsSettings:CustomerPoolConnectionString"];
        var exclusiveInsightsBotToken = builder.Configuration["ExclusiveInsightsSettings:DiscordBotToken"];

        //Singletons
        builder.Services.AddTransient<CommandProcessor>();
        builder.Services.AddSingleton<IntakeValidator>();
        builder.Services.AddSingleton<RequestThrottler>();
        if (backgroundHost && builder.Configuration.GetValue<bool>("Dispatcher:ServiceBusEnabled"))
        {
            if (string.IsNullOrWhiteSpace(serviceBusConnection))
                throw new InvalidOperationException("Service Bus connection is required when dispatch is enabled.");
            foreach (var queue in new[] { "EventReceivedQueue", "PlayerReceivedQueue", "LegendReceivedQueue" })
            {
                if (string.IsNullOrWhiteSpace(builder.Configuration["ServiceBusSettings:" + queue]))
                    throw new InvalidOperationException($"ServiceBusSettings:{queue} is required.");
            }
            builder.Services.AddSingleton(provider => new ServiceBusClient(serviceBusConnection));
            builder.Services.AddScoped<IAzureBusApiService, AzureBusApiService>();
            builder.Services.AddScoped<EventReceivedWorker>();
            builder.Services.AddScoped<PlayerReceivedWorker>();
            builder.Services.AddScoped<LegendReceivedWorker>();
            builder.Services.AddScoped<BracketOutboxWorker>();
        }
        else
        {
            if (backgroundHost && builder.Configuration.GetValue<bool>("Dispatcher:SchedulingEnabled"))
                throw new InvalidOperationException("Scheduled intake requires Dispatcher:ServiceBusEnabled.");
            builder.Services.AddScoped<IAzureBusApiService, DisabledBusApiService>();
        }
        if (backgroundHost && builder.Configuration.GetValue<bool>("Dispatcher:DiscordEnabled"))
        {
            builder.Services.AddSingleton(sp =>
            {
                var sourceBuilder = new NpgsqlDataSourceBuilder(customerPoolConnection);
                return sourceBuilder.Build();
            });
            builder.Services.AddSingleton<IDiscordRegistry, DiscordRegistry>();
            builder.Services.AddScoped<DiscordStartupService>();

            builder.Services.AddSingleton<ISocketEngine>(sp =>
            {
                var log = sp.GetRequiredService<ILogger<DiscordSocketEngine>>();
                var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
                return new DiscordSocketEngine(exclusiveInsightsBotToken!, customerPoolConnection!, log, scopeFactory);
            });
            builder.Services.AddSingleton<IEmailSender>(sp =>
                new SendGridEmailSender(
                    builder.Configuration["SendGridSettings:ApiKey"] ?? throw new InvalidOperationException("SendGrid API key is required for email reports."),
                    builder.Configuration["SendGridSettings:FromEmail"] ?? throw new InvalidOperationException("SendGrid sender email is required."),
                    builder.Configuration["SendGridSettings:FromName"] ?? "Sengoku"));
            builder.Services.AddSingleton<IGoogleSheetsClient>(sp =>
            {
                return new GoogleSheetsClient(new SheetsService());
            });
            builder.Services.AddSingleton<EventListenerManager>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<EventListenerManager>>();
                var socketEngine = provider.GetRequiredService<ISocketEngine>();
                var discordRegistry = provider.GetRequiredService<IDiscordRegistry>();
                return new EventListenerManager(logger!, socketEngine!, discordRegistry!);
            });

        }

        //Scopes
        builder.Services.AddScoped<IBracketTournamentBootstrap>(sp => new BracketTournamentBootstrap(connectionString!,
            sp.GetRequiredService<GraphQLHttpClient>(), sp.GetRequiredService<RequestThrottler>(), sp.GetRequiredService<IEventOperations>(), sp.GetRequiredService<ILogger<BracketTournamentBootstrap>>()));
        builder.Services.AddScoped<PlayerLegendLookup>(sp => new PlayerLegendLookup(connectionString!, sp.GetRequiredService<ILogger<PlayerLegendLookup>>()));
        builder.Services.AddScoped<IBracketCheckpointStore>(_ => new BracketCheckpointStore(connectionString!));
        builder.Services.AddScoped(provider => new GraphQLHttpClient(graphQLUrl, new NewtonsoftJsonSerializer())
        {
            HttpClient = { DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", bearerToken) } }
        });

        builder.Services.AddScoped<ICommonDatabaseService, CommonDatabaseService>(provider =>
        {
            return new CommonDatabaseService(connectionString);
        });
        builder.Services.AddScoped<IUserOperations, UserOperations>();
        builder.Services.AddScoped<IUserService, UserService>(provider =>
        {
            var intakeValidator = provider.GetRequiredService<IntakeValidator>();

            return new UserService(connectionString, intakeValidator);
        });
        if (backgroundHost)
        {
            builder.Services.AddHttpClient<IDiscordWebhookHandler, DiscordWebhookHandler>(client => client.Timeout = TimeSpan.FromSeconds(20))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                .RemoveAllLoggers();
        }
        builder.Services.AddScoped<IOrganizerQueryService, OrganizerQueryService>(provider =>
        {
            var graphQlClient = provider.GetRequiredService<GraphQLHttpClient>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            return new OrganizerQueryService(connectionString, graphQlClient, throttler, commonServices);
        });
        builder.Services.AddScoped<IOrganizerIntakeService, OrganizerOperations>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var graphClient = provider.GetRequiredService<GraphQLHttpClient>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var userService = provider.GetRequiredService<IUserService>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            return new OrganizerOperations(connectionString, graphClient, throttler, userService, commonServices);
        });
        builder.Services.AddScoped<IEventIntakeService>(_ => new EventIntakeService(connectionString));
        builder.Services.AddScoped<IEventOperations>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var intakeValidator = provider.GetRequiredService<IntakeValidator>();
            var graphQlClient = provider.GetRequiredService<GraphQLHttpClient>();
            var queryService = provider.GetRequiredService<IEventQueryService>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var serviceBus = provider.GetRequiredService<IAzureBusApiService>();
            var eventIntakeService = provider.GetRequiredService<IEventIntakeService>();
            return new EventOperations(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler, eventIntakeService);
        });
        builder.Services.AddScoped<ILegendQueryService, LegendQueryService>(provider =>
        {
            var graphQlClient = provider.GetRequiredService<GraphQLHttpClient>();
            var commonService = provider.GetRequiredService<ICommonDatabaseService>();
            var eventQueryService = provider.GetRequiredService<IEventQueryService>();
            return new LegendQueryService(connectionString, graphQlClient, commonService, eventQueryService);
        });
        builder.Services.AddScoped<IPlayerQueryService, PlayerQueryService>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var graphQlClient = provider.GetRequiredService<GraphQLHttpClient>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            return new PlayerQueryService(connectionString, configuration, graphQlClient, throttler, commonServices);
        });
        builder.Services.AddScoped<IPlayerIntakeService>(provider => new PlayerIntakeService(connectionString,
            provider.GetRequiredService<ICommonDatabaseService>(), provider.GetRequiredService<IEventQueryService>(),
            provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IAzureBusApiService>()));
        builder.Services.AddScoped<IPlayerOperations>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            var playerQueryService = provider.GetRequiredService<IPlayerQueryService>();
            var legendQueryService = provider.GetRequiredService<ILegendQueryService>();
            var eventQueryService = provider.GetRequiredService<IEventQueryService>();
            var serviceBus = provider.GetRequiredService<IAzureBusApiService>();
            var playerIntakeService = provider.GetRequiredService<IPlayerIntakeService>();
            var bracketCheckpointStore = provider.GetRequiredService<IBracketCheckpointStore>();
            return new PlayerOperations(connectionString, configuration, commonServices, playerQueryService, legendQueryService, eventQueryService, serviceBus, playerIntakeService, bracketCheckpointStore, provider.GetRequiredService<IBracketTournamentBootstrap>());
        });
        builder.Services.AddScoped<IEventQueryService, EventQueryService>(provider =>
        {
            var intakeValidator = provider.GetRequiredService<IntakeValidator>();
            var graphQlClient = provider.GetRequiredService<GraphQLHttpClient>();
            var throttler = provider.GetRequiredService<RequestThrottler>();
            var commonServices = provider.GetRequiredService<ICommonDatabaseService>();
            return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler, commonServices);
        });
        builder.Services.AddScoped<ILegendIntakeService, LegendIntakeService>(provider =>
        {
            var queryService = provider.GetRequiredService<ILegendQueryService>();
            return new LegendIntakeService(connectionString, queryService);
        });
        builder.Services.AddScoped<ILegendsOperations>(provider => new LegendsOperations(
            connectionString,
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILegendIntakeService>(),
            provider.GetRequiredService<ILegendQueryService>(),
            provider.GetRequiredService<IEventOperations>(),
            provider.GetRequiredService<IEventQueryService>(),
            provider.GetRequiredService<IPlayerQueryService>(),
            provider.GetRequiredService<IAzureBusApiService>(),
            provider.GetRequiredService<ICommonDatabaseService>(),
            provider.GetRequiredService<IUserService>()));
        if (backgroundHost && builder.Configuration.GetValue<bool>("Dispatcher:DiscordEnabled"))
        {
            builder.Services.AddScoped<ICustomerQueryService, CustomerQueryService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<CustomerQueryService>>();
                var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
                return new CustomerQueryService(dataSource!, logger);
            });
            builder.Services.AddScoped<ICustomerIntakeService, CustomerIntakeService>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
                var logger = provider.GetRequiredService<ILogger<CustomerIntakeService>>();
                var customerQuery = provider.GetRequiredService<ICustomerQueryService>();
                return new CustomerIntakeService(dataSource, logger, customerQuery);
            });
            builder.Services.AddScoped<ICustomerReportService, CustomerReportService>(sp =>
            {
                var customerQuery = sp.GetRequiredService<ICustomerQueryService>();
                var email = sp.GetRequiredService<IEmailSender>();
                var sheets = sp.GetRequiredService<IGoogleSheetsClient>();
                var log = sp.GetRequiredService<ILogger<CustomerReportService>>();

                return new CustomerReportService(customerQuery, email, sheets, log);
            });

        }
        builder.Services.AddScoped<IEventIntegrityService>(sp => new EventIntegrityService(
            sp.GetRequiredService<IEventQueryService>(), sp.GetRequiredService<IEventOperations>(), connectionString!));
        builder.Services.AddScoped<ILegendIntegrityService>(sp => new LegendIntegrityService(connectionString!,
            sp.GetRequiredService<ILegendQueryService>(), sp.GetRequiredService<ILegendIntakeService>()));
        builder.Services.AddScoped<IEventHandlerFactory, EventHandlerFactory>();
        builder.Services.AddScoped<IPlayerHandlerFactory, PlayerHandlerFactory>();
        builder.Services.AddScoped<ILegendHandlerFactory, LegendHandlerFactory>();
        if (backgroundHost) builder.Services.AddScoped<EventFeedWorker>();
        if (backgroundHost) builder.Services.AddScoped<ScheduledIntakeService>();
        if (backgroundHost) builder.Services.AddScoped<IntakeScheduleRunner>();
        builder.Services.AddHttpClient();
        if (!backgroundHost)
        {
            builder.AddDispatcherClient();
        }
    }
}

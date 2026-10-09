using SengokuProvider.API.Authentication;
using SengokuProvider.Library.Workflows.Users;
using Azure.Messaging.ServiceBus;
using ExcluSightsLibrary.DiscordServices;
using Google.Apis.Sheets.v4;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Npgsql;
using SengokuProvider.API;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Comms;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Orgs;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Library.Workflows.Legends;
using SengokuProvider.Library.Workflows.Events;
using SengokuProvider.Library.Workflows.Players;
using SengokuProvider.Library.Workflows.Orgs;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

//constant config variables
var connectionString = builder.Configuration["ConnectionStrings:AlexandriaConnectionString"];
var graphQLUrl = builder.Configuration["GraphQLSettings:Endpoint"];
var bearerToken = builder.Configuration["GraphQLSettings:Bearer"];
var serviceBusConnection = builder.Configuration["ServiceBusSettings:AzureWebJobsServiceBus"];
var customerPoolConnection = builder.Configuration["ExclusiveInsightsSettings:CustomerPoolConnectionString"];
var exclusiveInsightsBotToken = builder.Configuration["ExclusiveInsightsSettings:DiscordBotToken"];

//Singletons
builder.Services.AddSingleton(sp =>
{
    var sourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    return sourceBuilder.Build();
});
builder.Services.AddSingleton(sp =>
{
    var sourceBuilder = new NpgsqlDataSourceBuilder(customerPoolConnection);
    return sourceBuilder.Build();
});
builder.Services.AddTransient<CommandProcessor>();
builder.Services.AddSingleton<IntakeValidator>();
builder.Services.AddSingleton<RequestThrottler>();
builder.Services.AddSingleton(provider => { return new ServiceBusClient(serviceBusConnection); });
builder.Services.AddSingleton<IDiscordRegistry, DiscordRegistry>();

builder.Services.AddSingleton<ISocketEngine>(sp =>
{
    var log = sp.GetRequiredService<ILogger<DiscordSocketEngine>>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    return new DiscordSocketEngine(exclusiveInsightsBotToken!, customerPoolConnection!, log, scopeFactory);
});
builder.Services.AddSingleton<IEmailSender>(sp =>
    new SendGridEmailSender(
        builder.Configuration["SendGridSettings:ApiKey"],
        builder.Configuration["SendGridSettings:FromEmail"],
        builder.Configuration["SendGridSettings:FromName"]));
builder.Services.AddSingleton<IGoogleSheetsClient>(sp =>
{
    return new GoogleSheetsClient(new SheetsService());
});
builder.Services.AddSingleton<EventListenerManager>(provider =>
{
    var logger = provider.GetService<ILogger<EventListenerManager>>();
    var socketEngine = provider.GetService<ISocketEngine>();
    var discordRegistry = provider.GetService<IDiscordRegistry>();
    return new EventListenerManager(logger!, socketEngine!, discordRegistry!);
});

// start socket at app boot
builder.Services.AddHostedService<DiscordStartupService>();

builder.AddAccountAuthentication(connectionString!);

//Scopes
builder.Services.AddScoped<IBracketTournamentBootstrap>(sp => new BracketTournamentBootstrap(connectionString!,
    sp.GetRequiredService<GraphQLHttpClient>(), sp.GetRequiredService<RequestThrottler>(), sp.GetRequiredService<IEventOperations>()));
builder.Services.AddScoped<IBracketCheckpointStore>(_ => new BracketCheckpointStore(connectionString!));
builder.Services.AddScoped<IAzureBusApiService, AzureBusApiService>(provider =>
{
    var client = provider.GetService<ServiceBusClient>();
    return new AzureBusApiService(client);
});
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
builder.Services.AddScoped<IDiscordWebhookHandler, DiscordWebhookHandler>(provider =>
{
    return new DiscordWebhookHandler(connectionString);
});
builder.Services.AddScoped<IOrganizerQueryService, OrganizerQueryService>(provider =>
{
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var throttler = provider.GetService<RequestThrottler>();
    var commonServices = provider.GetService<ICommonDatabaseService>();
    return new OrganizerQueryService(connectionString, graphQlClient, throttler, commonServices);
});
builder.Services.AddScoped<IOrganizerIntakeService, OrganizerOperations>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var graphClient = provider.GetService<GraphQLHttpClient>();
    var throttler = provider.GetService<RequestThrottler>();
    var userService = provider.GetService<IUserService>();
    var commonServices = provider.GetService<ICommonDatabaseService>();
    return new OrganizerOperations(connectionString, graphClient, throttler, userService, commonServices);
});
builder.Services.AddScoped<IEventIntakeService>(_ => new EventIntakeService(connectionString));
builder.Services.AddScoped<IEventOperations>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var intakeValidator = provider.GetService<IntakeValidator>();
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var queryService = provider.GetService<IEventQueryService>();
    var throttler = provider.GetService<RequestThrottler>();
    var serviceBus = provider.GetService<IAzureBusApiService>();
    var eventIntakeService = provider.GetRequiredService<IEventIntakeService>();
    return new EventOperations(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler, eventIntakeService);
});
builder.Services.AddScoped<ILegendQueryService, LegendQueryService>(provider =>
{
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var commonService = provider.GetService<ICommonDatabaseService>();
    var eventQueryService = provider.GetService<IEventQueryService>();
    return new LegendQueryService(connectionString, graphQlClient, commonService, eventQueryService);
});
builder.Services.AddScoped<IPlayerQueryService, PlayerQueryService>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var throttler = provider.GetService<RequestThrottler>();
    var commonServices = provider.GetService<ICommonDatabaseService>();
    return new PlayerQueryService(connectionString, configuration, graphQlClient, throttler, commonServices);
});
builder.Services.AddScoped<IPlayerIntakeService>(provider => new PlayerIntakeService(connectionString,
    provider.GetRequiredService<ICommonDatabaseService>(), provider.GetRequiredService<IEventQueryService>(),
    provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IAzureBusApiService>()));
builder.Services.AddScoped<IPlayerOperations>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var commonServices = provider.GetService<ICommonDatabaseService>();
    var playerQueryService = provider.GetService<IPlayerQueryService>();
    var legendQueryService = provider.GetService<ILegendQueryService>();
    var eventQueryService = provider.GetService<IEventQueryService>();
    var serviceBus = provider.GetService<IAzureBusApiService>();
    var playerIntakeService = provider.GetRequiredService<IPlayerIntakeService>();
    var bracketCheckpointStore = provider.GetRequiredService<IBracketCheckpointStore>();
    return new PlayerOperations(connectionString, configuration, commonServices, playerQueryService, legendQueryService, eventQueryService, serviceBus, playerIntakeService, bracketCheckpointStore, provider.GetRequiredService<IBracketTournamentBootstrap>());
});
builder.Services.AddScoped<IEventQueryService, EventQueryService>(provider =>
{
    var intakeValidator = provider.GetService<IntakeValidator>();
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var throttler = provider.GetService<RequestThrottler>();
    var commonServices = provider.GetService<ICommonDatabaseService>();
    return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler, commonServices);
});
builder.Services.AddScoped<ILegendIntakeService, LegendIntakeService>(provider =>
{
    var queryService = provider.GetService<ILegendQueryService>();
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
builder.Services.AddScoped<ICustomerQueryService, CustomerQueryService>(provider =>
{
    var logger = provider.GetService<ILogger<CustomerQueryService>>();
    var dataSource = provider.GetService<NpgsqlDataSource>();
    return new CustomerQueryService(dataSource!, logger);
});
builder.Services.AddScoped<ICustomerIntakeService, CustomerIntakeService>(provider =>
{
    var config = provider.GetService<IConfiguration>();
    var dataSource = provider.GetService<NpgsqlDataSource>();
    var logger = provider.GetService<ILogger<CustomerIntakeService>>();
    var customerQuery = provider.GetService<ICustomerQueryService>();
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

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AccountFrontend");
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

using Azure.Messaging.ServiceBus;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Comms;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Library.Services.Orgs;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

//constant config variables
var connectionString = builder.Configuration["ConnectionStrings:AlexandriaConnectionString"];
var graphQLUrl = builder.Configuration["GraphQLSettings:Endpoint"];
var bearerToken = builder.Configuration["GraphQLSettings:Bearer"];
var serviceBusConnection = builder.Configuration["ServiceBusSettings:AzureWebJobsServiceBus"];

//Singletons
builder.Services.AddTransient<CommandProcessor>();
builder.Services.AddSingleton<IntakeValidator>();
builder.Services.AddSingleton<RequestThrottler>();
builder.Services.AddSingleton(provider => { return new ServiceBusClient(serviceBusConnection); });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

//Scopes
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
builder.Services.AddScoped<IOrganizerIntakeService, OrganizerIntakeService>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var graphClient = provider.GetService<GraphQLHttpClient>();
    var throttler = provider.GetService<RequestThrottler>();
    var userService = provider.GetService<IUserService>();
    var commonServices = provider.GetService<ICommonDatabaseService>();
    return new OrganizerIntakeService(connectionString, graphClient, throttler, userService, commonServices);
});
builder.Services.AddScoped<IEventIntakeService, EventIntakeService>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var intakeValidator = provider.GetService<IntakeValidator>();
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var queryService = provider.GetService<IEventQueryService>();
    var throttler = provider.GetService<RequestThrottler>();
    var serviceBus = provider.GetService<IAzureBusApiService>();
    return new EventIntakeService(connectionString, configuration, graphQlClient, queryService, serviceBus, intakeValidator, throttler);
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
builder.Services.AddScoped<IPlayerIntakeService, PlayerIntakeService>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var playerQueryService = provider.GetService<IPlayerQueryService>();
    var legendQueryService = provider.GetService<ILegendQueryService>();
    var eventQueryService = provider.GetService<IEventQueryService>();
    var serviceBus = provider.GetService<IAzureBusApiService>();
    return new PlayerIntakeService(connectionString, configuration, playerQueryService, legendQueryService, eventQueryService, serviceBus);
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
    var config = provider.GetService<IConfiguration>();
    var serviceBus = provider.GetService<IAzureBusApiService>();
    var eventQueryService = provider.GetService<IEventQueryService>();
    var userQueryService = provider.GetService<IUserService>();
    var playerQueryService = provider.GetService<IPlayerQueryService>();
    var commonServices = provider.GetService<ICommonDatabaseService>();
    return new LegendIntakeService(connectionString, config, queryService, eventQueryService, userQueryService, playerQueryService, serviceBus, commonServices);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAllOrigins");
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

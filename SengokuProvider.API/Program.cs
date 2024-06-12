using Azure.Messaging.ServiceBus;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
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
builder.Services.AddScoped<IEventIntakeService, EventIntakeService>(provider =>
{
    var intakeValidator = provider.GetService<IntakeValidator>();
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var queryService = provider.GetService<IEventQueryService>();
    var throttler = provider.GetService<RequestThrottler>();
    return new EventIntakeService(connectionString, graphQlClient, queryService, intakeValidator, throttler);
});
builder.Services.AddScoped<ILegendQueryService, LegendQueryService>(provider =>
{
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    return new LegendQueryService(connectionString, graphQlClient);
});
builder.Services.AddScoped<IPlayerQueryService, PlayerQueryService>(provider =>
{
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var throttler = provider.GetService<RequestThrottler>();
    return new PlayerQueryService(connectionString, graphQlClient, throttler);
});
builder.Services.AddScoped<IPlayerIntakeService, PlayerIntakeService>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var playerQueryService = provider.GetService<IPlayerQueryService>();
    var legendQueryService = provider.GetService<ILegendQueryService>();
    var serviceBus = provider.GetService<IAzureBusApiService>();
    return new PlayerIntakeService(connectionString, configuration, playerQueryService, legendQueryService, serviceBus);
});
builder.Services.AddScoped<IEventQueryService, EventQueryService>(provider =>
{
    var intakeValidator = provider.GetService<IntakeValidator>();
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    var throttler = provider.GetService<RequestThrottler>();
    return new EventQueryService(connectionString, graphQlClient, intakeValidator, throttler);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

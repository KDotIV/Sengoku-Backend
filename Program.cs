using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.API.Services.Common;
using SengokuProvider.API.Services.Events;
using SengokuProvider.API.Services.Users;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

//constant config variables
var connectionString = builder.Configuration.GetConnectionString("AlexandriaConnectionString");
var graphQLUrl = builder.Configuration["GraphQLSettings:Endpoint"];
var bearerToken = builder.Configuration["GraphQLSettings:Bearer"];

// Add services to the container.
builder.Services.AddTransient<CommandProcessor>();
builder.Services.AddSingleton<IntakeValidator>();
builder.Services.AddScoped<GraphQLHttpClient>(provider => new GraphQLHttpClient(graphQLUrl, new NewtonsoftJsonSerializer())
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
builder.Services.AddScoped<IEventService, EventService>(provider =>
{
    var intakeValidator = provider.GetService<IntakeValidator>();
    var graphQlClient = provider.GetService<GraphQLHttpClient>();
    return new EventService(connectionString, graphQlClient, intakeValidator);
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

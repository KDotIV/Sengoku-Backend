using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Worker.Factories;
using SengokuProvider.Worker.Handlers;
using System.Net.Http.Headers;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        services.AddHostedService<DataIntegrityWorker>();

        var connectionString = configuration.GetConnectionString("AlexandriaConnectionString");
        var graphQLUrl = configuration["GraphQLSettings:Endpoint"];
        var bearerToken = configuration["GraphQLSettings:Bearer"];

        // Add services to the container.
        services.AddSingleton<IEventIntegrityFactory, EventIntegrityFactory>();
        services.AddSingleton(provider => new GraphQLHttpClient(graphQLUrl, new NewtonsoftJsonSerializer())
        {
            HttpClient = { DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", bearerToken) } }
        });

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
            return new EventIntakeService(connectionString, graphQlClient, intakeValidator);
        });
        services.AddSingleton<IEventQueryService, EventQueryService>(provider =>
        {
            var intakeValidator = provider.GetService<IntakeValidator>();
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            return new EventQueryService(connectionString, graphQlClient, intakeValidator);
        });
        services.AddSingleton<IEventIntegrityService, EventIntegrityService>(provider =>
        {
            var graphQlClient = provider.GetService<GraphQLHttpClient>();
            return new EventIntegrityService(graphQlClient, connectionString);
        });
    })
    .Build();
await host.RunAsync();

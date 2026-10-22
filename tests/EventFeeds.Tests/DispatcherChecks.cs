using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SengokuDispatcher.API;
using SengokuDispatcher.Library.Configurations;
using SengokuDispatcher.Library.Handlers;
using SengokuDispatcher.Library.Models.Messaging;
using SengokuDispatcher.Library.Services.Common;
using SengokuDispatcher.Library.Services.Common.Interfaces;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuDispatcher.Library.Services.Scheduling;
using SengokuDispatcher.Library.Workflows.Players;
using SengokuProvider.Library.Configurations;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using Newtonsoft.Json;

static class DispatcherChecks
{
    public static async Task Run()
    {
        foreach (bool enabled in new[] { false, true })
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration["ConnectionStrings:AlexandriaConnectionString"] = "Host=localhost;Database=unused;Username=unused";
            builder.Configuration["GraphQLSettings:Endpoint"] = "https://example.test/graphql";
            builder.Configuration["GraphQLSettings:Bearer:0"] = "configured-test-token";
            builder.Configuration["Dispatcher:ServiceBusEnabled"] = enabled.ToString();
            builder.Configuration["Dispatcher:DiscordEnabled"] = "false";
            builder.Configuration["Dispatcher:SchedulingEnabled"] = "false";
            builder.Configuration["EventFeeds:DeliveryEnabled"] = "false";
            builder.Configuration["ServiceBusSettings:AzureWebJobsServiceBus"] =
                "Endpoint=sb://unused.servicebus.windows.net/;SharedAccessKeyName=unused;SharedAccessKey=dGVzdA==";
            foreach (string queue in new[] { "EventReceivedQueue", "PlayerReceivedQueue", "LegendReceivedQueue" })
                builder.Configuration["ServiceBusSettings:" + queue] = queue.ToLowerInvariant();
            builder.AddAccountAuthentication(builder.Configuration["ConnectionStrings:AlexandriaConnectionString"]!);
            builder.AddDispatcher();
            builder.AddEventFeeds();
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            await using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var scope = provider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            Check(services.GetRequiredService<GraphQL.Client.Http.GraphQLHttpClient>()
                .HttpClient.DefaultRequestHeaders.Authorization?.Parameter == "configured-test-token",
                "GraphQL client uses the configured bearer array");
            _ = services.GetRequiredService<IEventFeedStore>();
            _ = services.GetRequiredService<IDiscordWebhookConnection>();
            _ = services.GetRequiredService<IPlayerOperations>();
            if (enabled)
            {
                // Resolve without starting: no broker connections or external messages.
                _ = services.GetRequiredService<EventReceivedWorker>();
                _ = services.GetRequiredService<LegendReceivedWorker>();
                _ = services.GetRequiredService<BracketOutboxWorker>();
                _ = services.GetRequiredService<PlayerReceivedWorker>();
            }
            else
            {
                Check(services.GetRequiredService<IAzureBusApiService>() is DisabledBusApiService, "disabled publisher");
            }
        }

        var command = new PlayerReceivedData
        {
            Command = new ResumeBracketProcessingCommand { OperationId = Guid.NewGuid() },
            MessagePriority = MessagePriority.SystemIntake
        };
        var roundTrip = JsonConvert.DeserializeObject<PlayerReceivedData>(JsonConvert.SerializeObject(command),
            new JsonSerializerSettings { Converters = [new CommandSerializer()] });
        Check(roundTrip?.Command is ResumeBracketProcessingCommand resume && resume.OperationId == ((ResumeBracketProcessingCommand)command.Command).OperationId,
            "shared command serializer preserves message contract");
        DateTime midnight = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        Check(IntakeScheduleRunner.NextRun(midnight, 0, 6, 12, 18) == midnight.AddHours(6), "six-hour schedule");
        Check(IntakeScheduleRunner.NextRun(midnight.AddHours(14), 1, 13) == midnight.AddDays(1).AddHours(1), "next-day schedule");

        var collection = new ServiceCollection();
        collection.AddScoped<FailingWorker>();
        await using var owner = collection.BuildServiceProvider();
        await using var driver = new HostedServiceDriver<FailingWorker>(owner.GetRequiredService<IServiceScopeFactory>());
        try
        {
            await driver.StartAsync(default);
            await driver.ExecuteTask!;
            throw new Exception("Worker failure was swallowed.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "expected worker failure") { }
        Console.WriteLine("PASS Dispatcher dependency scopes, optional integrations, shared serialization, UTC schedules and worker failure propagation");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class FailingWorker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.FromException(new InvalidOperationException("expected worker failure"));
    }
}

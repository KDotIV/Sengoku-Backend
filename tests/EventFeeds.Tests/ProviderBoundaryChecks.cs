using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SengokuDispatcher.Library.Configurations;
using SengokuDispatcher.Library.Models.Feeds;
using SengokuDispatcher.Library.Services.Common.Interfaces;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuDispatcher.Library.Services.Remote;
using SengokuDispatcher.Library.Workflows.Events;
using SengokuProvider.Library.Configurations;

static class ProviderBoundaryChecks
{
    public static async Task Run()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["ConnectionStrings:AlexandriaConnectionString"] = "Host=localhost;Database=unused;Username=unused";
        builder.Configuration["GraphQLSettings:Endpoint"] = "https://example.test/graphql";
        builder.Configuration["Dispatcher:BaseUrl"] = "https://dispatcher.example.test";
        builder.Configuration["Dispatcher:ApiKey"] = new string('k', 32);
        // Even copied background settings must not start communications inside Provider.
        builder.Configuration["Dispatcher:ServiceBusEnabled"] = "true";
        builder.Configuration["Dispatcher:DiscordEnabled"] = "true";
        builder.Configuration["EventFeeds:DeliveryEnabled"] = "true";
        builder.AddAccountAuthentication(builder.Configuration["ConnectionStrings:AlexandriaConnectionString"]!);
        builder.AddDispatcher(backgroundHost: false);
        builder.AddEventFeeds(backgroundHost: false);
        await using var services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = services.CreateAsyncScope();
        Check(scope.ServiceProvider.GetRequiredService<IAzureBusApiService>() is RemoteBusApiService, "Provider uses a remote publisher");
        Check(scope.ServiceProvider.GetRequiredService<IDiscordFeedConnection>() is RemoteDiscordFeedConnection, "Provider delegates Discord OAuth");
        Check(scope.ServiceProvider.GetRequiredService<IDiscordWebhookConnection>() is RemoteDiscordWebhookConnection, "Provider delegates webhook verification");
        Check(scope.ServiceProvider.GetService<ServiceBusClient>() == null && scope.ServiceProvider.GetService<IEventFeedDispatcher>() == null, "Provider has no broker or delivery worker");
        _ = scope.ServiceProvider.GetRequiredService<IEventOperations>();

        var transport = new Transport();
        using var http = new HttpClient(transport);
        var client = new DispatcherClient(http, builder.Configuration, builder.Environment);
        var publisher = new RemoteBusApiService(client);
        Check(await publisher.SendBatchAsync("events", "{\"Command\":{}}"), "private publish acknowledgement");
        Check(transport.Path == "/internal/messages" && transport.Key == new string('k', 32), "private endpoint and server credential");
        using var body = JsonDocument.Parse(transport.Body!);
        Check(body.RootElement.GetProperty("queueName").GetString() == "events", "queue contract preserved");
        transport.Status = HttpStatusCode.Conflict;
        try
        {
            await client.Post<bool>("internal/discord/connect-webhook", new { });
            throw new Exception("Missing conflict translation");
        }
        catch (FeedConflictException) { }
        Check(transport.Calls == 2, "communication requests are not automatically retried");

        int accepted = 0;
        var middleware = new DispatcherAccessMiddleware(_ => { accepted++; return Task.CompletedTask; }, builder.Configuration, NullLogger<DispatcherAccessMiddleware>.Instance);
        var rejected = new DefaultHttpContext();
        rejected.Response.Body = new MemoryStream();
        rejected.Request.Path = "/internal/messages";
        await middleware.InvokeAsync(rejected);
        Check(rejected.Response.StatusCode == 401 && accepted == 0, "browser requests cannot access internal controls");
        var allowed = new DefaultHttpContext();
        allowed.Request.Path = "/internal/messages";
        allowed.Request.Headers["X-Dispatcher-Key"] = new string('k', 32);
        await middleware.InvokeAsync(allowed);
        Check(accepted == 1, "authenticated service request accepted");

        Check(typeof(SengokuProvider.API.Controllers.EventFeedsController).Assembly.GetName().Name == "SengokuProvider.API", "frontend feed routes remain Provider-owned");
        Check(!typeof(SengokuDispatcher.API.Controllers.CommunicationController).Assembly.GetTypes().Any(t => t.Name == "EventFeedsController" || t.Name == "PlayerController"), "Dispatcher has no frontend feed/player controllers");
        Console.WriteLine("PASS Provider frontend ownership, remote communications, private authentication and no automatic send retries");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Transport : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? Path, Key, Body;
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri!.AbsolutePath;
            Key = request.Headers.GetValues("X-Dispatcher-Key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = Status == HttpStatusCode.OK ? JsonContent.Create(true) : JsonContent.Create(new { message = "Version changed." })
            };
        }
    }
}

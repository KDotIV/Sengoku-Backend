using Microsoft.AspNetCore.Mvc;
using SengokuDispatcher.Library.Models.Messaging;
using SengokuDispatcher.Library.Services.Common.Interfaces;
using SengokuDispatcher.Library.Services.Comms;
using SengokuDispatcher.Library.Services.Feeds;

namespace SengokuDispatcher.API.Controllers;

/// <summary>Private control endpoints called by Provider; never a frontend API.</summary>
[ApiController, Route("internal")]
public sealed class CommunicationController : ControllerBase
{
    [HttpPost("messages")]
    public async Task<IActionResult> Publish(PublishCommandRequest request, [FromServices] IAzureBusApiService bus, [FromServices] IConfiguration configuration)
    {
        var queues = new[] { "EventReceivedQueue", "PlayerReceivedQueue", "LegendReceivedQueue" }
            .Select(name => configuration["ServiceBusSettings:" + name]);
        if (string.IsNullOrWhiteSpace(request.QueueName) || !queues.Contains(request.QueueName) || string.IsNullOrWhiteSpace(request.Payload))
            return BadRequest(new DispatcherError("A configured intake queue and payload are required."));
        return Ok(await bus.SendBatchAsync(request.QueueName, request.Payload));
    }

    [HttpPost("discord/begin")]
    public async Task<IActionResult> Begin(BeginDiscordRequest request, [FromServices] IDiscordFeedConnection connection, CancellationToken ct) =>
        new JsonResult(await connection.Begin(request.UserId, request.SubscriptionId, request.SessionToken, ct));

    [HttpPost("discord/complete")]
    public async Task<IActionResult> Complete(CompleteDiscordRequest request, [FromServices] IDiscordFeedConnection connection, CancellationToken ct) =>
        Ok(await connection.Complete(request.UserId, request.SessionToken, request.State, request.Code, request.Error, ct));

    [HttpPost("discord/connect-webhook")]
    public async Task<IActionResult> Connect(ConnectWebhookRequest request, [FromServices] IDiscordWebhookConnection connection, CancellationToken ct)
    {
        var subscription = await connection.Connect(request.UserId, request.Connection, ct);
        return subscription == null ? NotFound() : Ok(subscription);
    }

    [HttpPost("discord/send-webhook")]
    public async Task<IActionResult> SendWebhook(SendWebhookRequest request, [FromServices] IDiscordWebhookHandler handler) =>
        Ok(await handler.SendLeaderboardUpdateMessage(request.WebhookUrl, request.MessageContent, request.RoleIds, request.Attachments));

    [HttpPost("discord/send-thread")]
    public async Task<IActionResult> SendThread(SendThreadRequest request, [FromServices] IDiscordWebhookHandler handler)
    {
        await handler.SendThreadUpdateMessage(request.GameId, request.MessageContent, request.ThreadId, request.Attachments);
        return Ok(true);
    }
}

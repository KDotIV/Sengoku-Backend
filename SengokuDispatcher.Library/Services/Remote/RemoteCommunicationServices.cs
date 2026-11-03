using SengokuDispatcher.Library.Models.Feeds;
using SengokuDispatcher.Library.Models.Messaging;
using SengokuDispatcher.Library.Services.Common.Interfaces;
using SengokuDispatcher.Library.Services.Comms;
using SengokuDispatcher.Library.Services.Feeds;

namespace SengokuDispatcher.Library.Services.Remote;

public sealed class RemoteBusApiService(DispatcherClient dispatcher) : IAzureBusApiService
{
    public Task<bool> SendBatchAsync(string? queueName, string messages)
    {
        if (string.IsNullOrWhiteSpace(queueName) || string.IsNullOrWhiteSpace(messages)) return Task.FromResult(false);
        return dispatcher.Post<bool>("internal/messages", new PublishCommandRequest(queueName, messages));
    }
}

public sealed class RemoteDiscordFeedConnection(DispatcherClient dispatcher, IConfiguration config) : IDiscordFeedConnection
{
    public bool Enabled => config.GetValue<bool>("Dispatcher:DiscordOAuthEnabled");

    public async Task<string> Begin(int userId, Guid subscriptionId, string sessionToken, CancellationToken ct)
    {
        return await dispatcher.Post<string>("internal/discord/begin", new BeginDiscordRequest(userId, subscriptionId, sessionToken), ct)
            ?? throw new InvalidOperationException("Discord authorization was not created.");
    }

    public async Task<EventFeedSubscriptionView> Complete(int userId, string sessionToken, string? state, string? code, string? error, CancellationToken ct)
    {
        return await dispatcher.Post<EventFeedSubscriptionView>("internal/discord/complete", new CompleteDiscordRequest(userId, sessionToken, state, code, error), ct)
            ?? throw new ArgumentException("Subscription unavailable.");
    }
}

public sealed class RemoteDiscordWebhookConnection(DispatcherClient dispatcher) : IDiscordWebhookConnection
{
    public Task<EventFeedSubscriptionView?> Connect(int userId, ConnectDiscordWebhookRequest request, CancellationToken ct)
    {
        return dispatcher.Post<EventFeedSubscriptionView>("internal/discord/connect-webhook", new ConnectWebhookRequest(userId, request), ct);
    }
}

public sealed class RemoteDiscordWebhookHandler(DispatcherClient dispatcher) : IDiscordWebhookHandler
{
    public async Task SendThreadUpdateMessage(int gameId, string messageContent, long theadId, object? attachments = default)
    {
        await dispatcher.Post<bool>("internal/discord/send-thread", new SendThreadRequest(gameId, messageContent, theadId, attachments));
    }

    public Task<bool> SendLeaderboardUpdateMessage(string webhookUrl, string messageContent, string[] roleId, object? attachments = default)
    {
        return dispatcher.Post<bool>("internal/discord/send-webhook", new SendWebhookRequest(webhookUrl, messageContent, roleId, attachments));
    }
}

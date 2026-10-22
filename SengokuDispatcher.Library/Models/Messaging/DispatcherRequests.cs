using SengokuDispatcher.Library.Services.Feeds;

namespace SengokuDispatcher.Library.Models.Messaging;

public sealed record PublishCommandRequest(string QueueName, string Payload);
public sealed record BeginDiscordRequest(int UserId, Guid SubscriptionId, string SessionToken);
public sealed record CompleteDiscordRequest(int UserId, string SessionToken, string? State, string? Code, string? Error);
public sealed record ConnectWebhookRequest(int UserId, ConnectDiscordWebhookRequest Connection);
public sealed record SendWebhookRequest(string WebhookUrl, string MessageContent, string[] RoleIds, object? Attachments);
public sealed record SendThreadRequest(int GameId, string MessageContent, long ThreadId, object? Attachments);
public sealed record DispatcherError(string Message);

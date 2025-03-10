﻿using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Services.Comms
{
    public interface IDiscordWebhookHandler
    {
        public Task SendThreadUpdateMessage(int gameId, string messageContent, long theadId, object? attachments = default);
        public Task<bool> SendLeaderboardUpdateMessage(string WebhookUrl, string messageContent, string[] roleId, object? attachments = default);
        public Task<bool> SubscribeToFeed(string serverName, string subscribedChannel, string webhookUrl, string feedId);
        public Task<FeedData> GetFeedById(string feedId);
    }
}
using SengokuProvider.Library.Models.Contracts;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Worker.Publishers;
using System.Text.Json;

namespace SengokuProvider.Worker.Jobs
{
    public sealed class IngestionJobPublisher : IIngestionJobPublisher
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
        private readonly IAzureBusApiService _bus;
        private readonly IConfiguration _configuration;

        public IngestionJobPublisher(IAzureBusApiService bus, IConfiguration configuration)
        {
            _bus = bus;
            _configuration = configuration;
        }

        public Task<Guid> EnqueueAsync(IntakeEventsByLocationCommand command, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:EventReceivedQueue",
                IngestionJobType.ImportEventsByLocation,
                new ImportEventsByLocationPayload(command.PerPage, command.StateCode!, command.StartDate, command.EndDate, command.Filters, command.VariableDefinitions),
                correlationId,
                cancellationToken);

        public Task<Guid> EnqueueAsync(IntakeEventsByGameIdCommand command, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:EventReceivedQueue",
                IngestionJobType.ImportEventsByGame,
                new ImportEventsByGamePayload(command.Page, command.StateCode!, command.StartDate, command.EndDate, command.GameIDs),
                correlationId,
                cancellationToken);

        public Task<Guid> EnqueueAsync(LinkTournamentByEventIdCommand command, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:EventReceivedQueue",
                IngestionJobType.LinkTournamentByEvent,
                new LinkTournamentByEventPayload(command.EventLinkId),
                correlationId,
                cancellationToken);

        public Task<Guid> EnqueueAsync(IntakePlayersByTournamentCommand command, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:PlayerReceivedQueue",
                IngestionJobType.ImportPlayersByTournament,
                new ImportPlayersByTournamentPayload(command.TournamentLink),
                correlationId,
                cancellationToken);

        public Task<Guid> EnqueueAsync(OnboardPlayerDataCommand command, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:PlayerReceivedQueue",
                IngestionJobType.ImportPlayerHistory,
                new ImportPlayerHistoryPayload(command.PlayerId, command.GamerTag, command.PerPage),
                correlationId,
                cancellationToken);

        public Task<Guid> EnqueueAsync(OnboardBracketRunnerByBracketSlug command, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:PlayerReceivedQueue",
                IngestionJobType.BuildBracketSnapshot,
                new BuildBracketSnapshotPayload(command.BracketSlug, command.PlayerId),
                correlationId,
                cancellationToken);

        public Task<Guid> EnqueueUserLinkAsync(string playerName, string userSlug, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:PlayerReceivedQueue",
                IngestionJobType.LinkStartGgUser,
                new LinkStartGgUserPayload(playerName, userSlug),
                correlationId,
                cancellationToken);

        public Task<Guid> EnqueueAsync(OnboardTournamentStandingstoLeague command, string? correlationId = null, CancellationToken cancellationToken = default) =>
            PublishAsync(
                "ServiceBusSettings:LegendReceivedQueue",
                IngestionJobType.ImportTournamentStandingsToLeague,
                new ImportTournamentStandingsToLeaguePayload(command.TournamentLinks, command.EventLinkSlug, command.GameIds, command.LeagueId, command.Open),
                correlationId,
                cancellationToken);

        private async Task<Guid> PublishAsync<T>(
            string queueConfigurationKey,
            IngestionJobType jobType,
            T payload,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            var queueName = _configuration[queueConfigurationKey]
                ?? _configuration[queueConfigurationKey[(queueConfigurationKey.LastIndexOf(':') + 1)..]]
                ?? throw new InvalidOperationException($"Missing queue configuration: {queueConfigurationKey}");
            var envelope = IngestionJobEnvelope.Create(jobType, payload, correlationId);
            await _bus.SendAsync(queueName, JsonSerializer.Serialize(envelope, SerializerOptions), envelope.MessageId.ToString("N"), cancellationToken);
            return envelope.MessageId;
        }
    }
}

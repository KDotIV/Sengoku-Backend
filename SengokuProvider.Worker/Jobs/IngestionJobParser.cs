using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Contracts;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Worker.Handlers;
using System.Text.Json;

namespace SengokuProvider.Worker.Jobs;

internal static class IngestionJobParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(string json, out IngestionJobEnvelope? envelope)
    {
        try
        {
            envelope = JsonSerializer.Deserialize<IngestionJobEnvelope>(json, Options);
            return envelope is not null && envelope.SchemaVersion == IngestionJobEnvelope.CurrentSchemaVersion;
        }
        catch (JsonException)
        {
            envelope = null;
            return false;
        }
    }

    public static EventReceivedData ToEventMessage(IngestionJobEnvelope envelope)
    {
        ICommand command = envelope.JobType switch
        {
            IngestionJobType.ImportEventsByLocation => ToLocationCommand(envelope.ReadPayload<ImportEventsByLocationPayload>()),
            IngestionJobType.ImportEventsByGame => ToGameCommand(envelope.ReadPayload<ImportEventsByGamePayload>()),
            IngestionJobType.LinkTournamentByEvent => new LinkTournamentByEventIdCommand
            {
                EventLinkId = envelope.ReadPayload<LinkTournamentByEventPayload>().EventLinkId,
                Topic = CommandRegistry.LinkTournamentByEvent
            },
            _ => throw new NotSupportedException($"{envelope.JobType} is not an event-ingestion job.")
        };

        return new EventReceivedData { Command = command, MessagePriority = MessagePriority.UserIntake };
    }

    public static PlayerReceivedData ToPlayerMessage(IngestionJobEnvelope envelope)
    {
        ICommand command = envelope.JobType switch
        {
            IngestionJobType.ImportPlayersByTournament => new IntakePlayersByTournamentCommand
            {
                TournamentLink = envelope.ReadPayload<ImportPlayersByTournamentPayload>().TournamentLink,
                Topic = CommandRegistry.IntakePlayersByTournament
            },
            IngestionJobType.ImportPlayerHistory => ToPlayerHistoryCommand(envelope.ReadPayload<ImportPlayerHistoryPayload>()),
            IngestionJobType.BuildBracketSnapshot => ToBracketCommand(envelope.ReadPayload<BuildBracketSnapshotPayload>()),
            IngestionJobType.LinkStartGgUser => ToUserLinkCommand(envelope.ReadPayload<LinkStartGgUserPayload>()),
            _ => throw new NotSupportedException($"{envelope.JobType} is not a player-ingestion job.")
        };

        return new PlayerReceivedData { Command = command, MessagePriority = MessagePriority.UserIntake };
    }

    public static OnboardReceivedData ToLegendMessage(IngestionJobEnvelope envelope)
    {
        if (envelope.JobType != IngestionJobType.ImportTournamentStandingsToLeague)
            throw new NotSupportedException($"{envelope.JobType} is not a league-ingestion job.");

        var payload = envelope.ReadPayload<ImportTournamentStandingsToLeaguePayload>();
        return new OnboardReceivedData
        {
            MessagePriority = MessagePriority.UserIntake,
            Command = new OnboardTournamentStandingstoLeague
            {
                TournamentLinks = payload.TournamentLinks,
                EventLinkSlug = payload.EventLinkSlug,
                GameIds = payload.GameIds,
                LeagueId = payload.LeagueId,
                Open = payload.Open,
                Topic = CommandRegistry.ImportTournamentStandingsToLeague
            }
        };
    }

    private static IntakeEventsByLocationCommand ToLocationCommand(ImportEventsByLocationPayload payload) => new()
    {
        PerPage = payload.PerPage,
        StateCode = payload.StateCode,
        StartDate = payload.StartDate,
        EndDate = payload.EndDate,
        Filters = payload.Filters,
        VariableDefinitions = payload.VariableDefinitions,
        Topic = CommandRegistry.IntakeEventsByLocation
    };

    private static IntakeEventsByGameIdCommand ToGameCommand(ImportEventsByGamePayload payload) => new()
    {
        Page = payload.Page,
        StateCode = payload.StateCode,
        StartDate = payload.StartDate,
        EndDate = payload.EndDate,
        GameIDs = payload.GameIds,
        Topic = CommandRegistry.IntakeEventsByGames
    };

    private static OnboardPlayerDataCommand ToPlayerHistoryCommand(ImportPlayerHistoryPayload payload) => new()
    {
        PlayerId = payload.PlayerId,
        GamerTag = payload.GamerTag,
        PerPage = payload.PerPage,
        Topic = CommandRegistry.OnboardPlayerData
    };

    private static OnboardBracketRunnerByBracketSlug ToBracketCommand(BuildBracketSnapshotPayload payload) => new()
    {
        BracketSlug = payload.BracketSlug,
        PlayerId = payload.PlayerId,
        Topic = CommandRegistry.BuildBracketSnapshot
    };

    private static LinkStartGgUserCommand ToUserLinkCommand(LinkStartGgUserPayload payload) => new()
    {
        PlayerName = payload.PlayerName,
        UserSlug = payload.UserSlug,
        Topic = CommandRegistry.LinkStartGgUser
    };
}
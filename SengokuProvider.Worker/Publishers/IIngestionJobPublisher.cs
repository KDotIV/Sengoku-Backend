using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Worker.Publishers
{
    public interface IIngestionJobPublisher
    {
        Task<Guid> EnqueueAsync(IntakeEventsByLocationCommand command, string? correlationId = null, CancellationToken cancellationToken = default);
        Task<Guid> EnqueueAsync(IntakeEventsByGameIdCommand command, string? correlationId = null, CancellationToken cancellationToken = default);
        Task<Guid> EnqueueAsync(LinkTournamentByEventIdCommand command, string? correlationId = null, CancellationToken cancellationToken = default);
        Task<Guid> EnqueueAsync(IntakePlayersByTournamentCommand command, string? correlationId = null, CancellationToken cancellationToken = default);
        Task<Guid> EnqueueAsync(OnboardPlayerDataCommand command, string? correlationId = null, CancellationToken cancellationToken = default);
        Task<Guid> EnqueueAsync(OnboardBracketRunnerByBracketSlug command, string? correlationId = null, CancellationToken cancellationToken = default);
        Task<Guid> EnqueueUserLinkAsync(string playerName, string userSlug, string? correlationId = null, CancellationToken cancellationToken = default);
        Task<Guid> EnqueueAsync(OnboardTournamentStandingstoLeague command, string? correlationId = null, CancellationToken cancellationToken = default);
    }
}
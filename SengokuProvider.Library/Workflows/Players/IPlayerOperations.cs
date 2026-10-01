using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.Leagues;
namespace SengokuProvider.Library.Workflows.Players;
public interface IPlayerOperations
{
    Task<bool> SendPlayerIntakeMessage(int tournamentLink);
    Task<int> IntakePlayerData(int tournamentLink);
    Task<int> OnboardPreviousTournamentData(OnboardPlayerDataCommand command, int volumeLimit = 100);
    Task<PlayerOnboardResult> OnboardBracketRunnerByBracketSlug(string bracketSlug, int playerId);
    Task<PlayerOnboardResult?> ResumeBracketProcessing(Guid operationId);
    Task<PlayerOnboardResult?> GetBracketProcessingStatus(Guid operationId);
}

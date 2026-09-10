using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
namespace SengokuProvider.Library.Workflows.Events;
public interface IEventOperations
{
    Task<List<int>> IntakeTournamentData(IntakeEventsByLocationCommand command);
    Task<int> IntakeTournamentIdData(LinkTournamentByEventIdCommand command);
    Task<int> IntakeEventsByGameId(IntakeEventsByGameIdCommand command);
    Task<int> IntakeTournamentsByLinkId(int[] tournamentLinks);
    Task<bool> SendTournamentLinkEventMessage(int eventLinkId);
    Task<bool> SendEventIntakeLocationMessage(IntakeEventsByLocationCommand command);
    Task<bool> UpdateEventData(UpdateEventCommand command);
    Task<string> IntakeNewRegion(AddressData addressData);
}

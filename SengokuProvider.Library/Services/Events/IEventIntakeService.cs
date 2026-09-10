using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Regions;
namespace SengokuProvider.Library.Services.Events;
public interface IEventIntakeService
{
    Task<int> InsertNewTournamentData(int totalSuccess, List<TournamentData> currentBatch);
    Task<int> InsertNewAddressData(List<AddressData> data);
    Task<int> InsertNewEventsData(List<EventData> data);
    Task<int> InsertNewRegionData(RegionData newData);
    Task<bool> UpdateEventData(UpdateEventCommand command);
}

using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Services.Comms.StartGG.Interfaces
{
    public interface IStartGgGraphQlClient
    {
        Task<T?> QueryAsync<T>(string query, object variables, CancellationToken cancellationToken = default);
    }
}
using SengokuProvider.Library.Services.Players;
using SengokuDispatcher.Library.Workflows.Players;
using SengokuDispatcher.Library.Factories;

public class PlayerHandlerFactory : IPlayerHandlerFactory
{
    private readonly IServiceProvider _services;

    public PlayerHandlerFactory(IServiceProvider services)
    {
        _services = services;
    }
    public IPlayerOperations CreateIntakeHandler()
    {
        var scope = _services;
        return scope.GetRequiredService<IPlayerOperations>();
    }

    public IPlayerIntegrityService CreateIntegrityHandler()
    {
        var scope = _services;
        return scope.GetRequiredService<IPlayerIntegrityService>();
    }

    public IPlayerQueryService CreateQueryHandler()
    {
        var scope = _services;
        return scope.GetRequiredService<IPlayerQueryService>();
    }
}

using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Workflows.Players;
using SengokuProvider.Worker.Factories;

public class PlayerHandlerFactory : IPlayerHandlerFactory
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public PlayerHandlerFactory(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    public IPlayerOperations CreateIntakeHandler()
    {
        var scope = _serviceScopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPlayerOperations>();
    }

    public IPlayerIntegrityService CreateIntegrityHandler()
    {
        var scope = _serviceScopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPlayerIntegrityService>();
    }

    public IPlayerQueryService CreateQueryHandler()
    {
        var scope = _serviceScopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPlayerQueryService>();
    }
}

using SengokuProvider.Library.Services.Players;
using SengokuProvider.Worker.Factories;

public class PlayerHandlerFactory : IPlayerHandlerFactory
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public PlayerHandlerFactory(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    public SengokuProvider.Library.Workflows.Players.IPlayerOperations CreateIntakeHandler()
    {
        var scope = _serviceScopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SengokuProvider.Library.Workflows.Players.IPlayerOperations>();
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

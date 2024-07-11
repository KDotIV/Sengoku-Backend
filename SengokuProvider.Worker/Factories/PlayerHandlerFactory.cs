using SengokuProvider.Library.Services.Players;
using SengokuProvider.Worker.Factories;

public class PlayerHandlerFactory : IPlayerHandlerFactory
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public PlayerHandlerFactory(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    public IPlayerIntakeService CreateIntakeHandler()
    {
        var scope = _serviceScopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPlayerIntakeService>();
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
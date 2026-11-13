using Npgsql;

namespace SengokuDispatcher.Library.Configurations;

/// <summary>Prevents accidental use of the shared QA database by a local Dispatcher.
/// App Service identity is an operational guard, not a replacement for authentication.</summary>
public static class DispatcherEnvironmentGuard
{
    public static void Validate(IConfiguration configuration)
    {
        var database = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("AlexandriaConnectionString"));
        var qa = (database.Host ?? "").Split(',').Any(host => host.Trim().Equals(
            "alexandia-qa.postgres.database.azure.com", StringComparison.OrdinalIgnoreCase))
            && string.Equals(database.Database, "qa", StringComparison.OrdinalIgnoreCase);
        if (qa && !string.Equals(configuration["WEBSITE_SITE_NAME"], "sengoku-dispatcher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the sengoku-dispatcher Azure App Service may run Dispatcher against QA. Use the disposable local test environment.");
    }
}

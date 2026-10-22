namespace SengokuDispatcher.Library.Services.Scheduling;

/// <summary>Runs the former Functions schedules in UTC without overlapping a local cycle.</summary>
public sealed class IntakeScheduleRunner(
    IServiceScopeFactory scopes,
    ILogger<IntakeScheduleRunner> logger) : BackgroundService
{
    public static DateTime NextRun(DateTime nowUtc, params int[] hours)
    {
        foreach (int hour in hours.Order())
        {
            DateTime candidate = nowUtc.Date.AddHours(hour);
            if (candidate > nowUtc)
            {
                return candidate;
            }
        }
        return nowUtc.Date.AddDays(1).AddHours(hours.Min());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Preserve the two former RunOnStartup jobs; circuit scheduling waits for its UTC slot.
        DateTime eventsAt = DateTime.UtcNow;
        DateTime standingsAt = eventsAt;
        DateTime circuitsAt = NextRun(eventsAt, 2, 13);
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime now = DateTime.UtcNow;
            if (eventsAt <= now)
            {
                await Run("EventIntake", (service, ct) => service.RunEventIntakeScheduler(ct), stoppingToken);
                eventsAt = NextRun(DateTime.UtcNow, 1, 13);
            }
            if (standingsAt <= now)
            {
                await Run("TournamentStandings", (service, ct) => service.RunTournamentUpdate(ct), stoppingToken);
                standingsAt = NextRun(DateTime.UtcNow, 0, 6, 12, 18);
            }
            if (circuitsAt <= now)
            {
                await Run("CircuitSchedule", (service, ct) => service.RunCircuitScheduleUpdate(ct), stoppingToken);
                circuitsAt = NextRun(DateTime.UtcNow, 2, 13);
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task Run(string job, Func<ScheduledIntakeService, CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            logger.LogInformation("Starting scheduled job {Job}", job);
            await action(scope.ServiceProvider.GetRequiredService<ScheduledIntakeService>(), ct);
            logger.LogInformation("Completed scheduled job {Job}", job);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Npgsql.NpgsqlException ex)
        {
            logger.LogError("Scheduled job {Job} failed accessing PostgreSQL (SQLSTATE {SqlState})", job, ex.SqlState);
        }
        catch (Exception ex)
        {
            logger.LogError("Scheduled job {Job} failed ({ErrorType})", job, ex.GetType().Name);
        }
    }
}

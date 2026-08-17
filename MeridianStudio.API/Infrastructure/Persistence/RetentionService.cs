using MeridianStudio.API.Application.Interfaces;

namespace MeridianStudio.API.Infrastructure.Persistence;

/// <summary>
/// Background retention purge: removes artifacts older than <c>Persistence:RetentionDays</c>.
/// Only registered when RetentionDays &gt; 0 (0/absent = keep forever). Runs once at startup
/// then daily. Restores the self-expiry property the durable store removed vs the old TTL cache.
/// </summary>
public sealed class RetentionService(
    IArtifactStore store, IConfiguration config, ILogger<RetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var days = config.GetValue("Persistence:RetentionDays", 0);
        if (days <= 0) return; // safety — should not be registered in this case

        var interval = TimeSpan.FromHours(config.GetValue("Persistence:RetentionSweepHours", 24.0));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
                var removed = await store.PurgeOlderThanAsync(cutoff, stoppingToken);
                if (removed > 0)
                    logger.LogInformation("[Retention] Purged {Count} artifact(s) older than {Days}d.", removed, days);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Retention] Sweep failed — will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

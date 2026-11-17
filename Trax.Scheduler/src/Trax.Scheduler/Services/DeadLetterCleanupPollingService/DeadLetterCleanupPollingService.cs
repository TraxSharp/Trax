using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.DeadLetterCleanup;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.DeadLetterCleanupPollingService;

/// <summary>
/// Background service that periodically runs the dead letter cleanup train.
/// </summary>
internal sealed class DeadLetterCleanupPollingService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    ILogger<DeadLetterCleanupPollingService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "DeadLetterCleanupPollingService starting with interval {Interval}, retention {Retention}",
            configuration.DeadLetterCleanupInterval,
            configuration.DeadLetterRetentionPeriod
        );

        // Run immediately on startup
        await RunCleanup(stoppingToken).ConfigureAwait(false);

        while (
            await PollingDelay
                .WaitAsync(() => configuration.DeadLetterCleanupInterval, stoppingToken)
                .ConfigureAwait(false)
        )
        {
            await RunCleanup(stoppingToken).ConfigureAwait(false);
        }

        logger.LogInformation("DeadLetterCleanupPollingService stopping");
    }

    private async Task RunCleanup(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var train = scope.ServiceProvider.GetRequiredService<IDeadLetterCleanupTrain>();

            logger.LogDebug("Dead letter cleanup cycle starting");
            await train
                .Run(new DeadLetterCleanupRequest(), cancellationToken)
                .ConfigureAwait(false);
            logger.LogDebug("Dead letter cleanup cycle completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during dead letter cleanup cycle");
        }
    }
}

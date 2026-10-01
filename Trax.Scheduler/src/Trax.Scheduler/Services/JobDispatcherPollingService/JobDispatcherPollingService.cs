using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.JobDispatcherPollingService;

/// <summary>
/// Background service that polls the work queue on a configurable interval
/// and dispatches queued jobs via <see cref="IJobDispatcherTrain"/>.
/// </summary>
internal class JobDispatcherPollingService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    SchedulerLivenessMonitor livenessMonitor,
    ILogger<JobDispatcherPollingService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "JobDispatcherPollingService starting with polling interval {Interval}",
            configuration.JobDispatcherPollingInterval
        );

        await RunJobDispatcher(stoppingToken);

        // The interval is read each cycle, so a runtime change applies to the next wait.
        while (
            await PollingDelay.WaitAsync(
                () => configuration.JobDispatcherPollingInterval,
                stoppingToken
            )
        )
        {
            await RunJobDispatcher(stoppingToken);
        }

        logger.LogInformation("JobDispatcherPollingService stopping");
    }

    private async Task RunJobDispatcher(CancellationToken cancellationToken)
    {
        if (!configuration.JobDispatcherEnabled)
        {
            // The loop is alive and doing what it was told; stamping it means re-enabling the
            // dispatcher does not start from a stale timestamp. The health check reports "paused".
            livenessMonitor.RecordDispatchCycle();
            logger.LogDebug("JobDispatcher is disabled, skipping polling cycle");
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var train = scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

            logger.LogDebug("JobDispatcher polling cycle starting");
            livenessMonitor.BeginDispatchCycle();
            await train.Run(Unit.Default, cancellationToken);

            // Stamp completion only on a successful cycle (a no-op poll still proves the loop
            // and DB round-trip work). A failed run leaves the timestamp stale so the health
            // check flips unhealthy.
            livenessMonitor.RecordDispatchCycle();
            logger.LogDebug("JobDispatcher polling cycle completed");
        }
        catch (Exception ex)
        {
            livenessMonitor.RecordDispatchCycleFailed();
            logger.LogError(ex, "Error during JobDispatcher polling cycle");
        }
    }
}

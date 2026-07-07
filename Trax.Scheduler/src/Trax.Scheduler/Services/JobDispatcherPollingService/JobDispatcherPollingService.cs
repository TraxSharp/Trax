using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Services.JobDispatcherPollingService;

/// <summary>
/// Background service that polls the work queue on a configurable interval
/// and dispatches queued jobs via <see cref="IJobDispatcherTrain"/>.
/// </summary>
internal class JobDispatcherPollingService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    ISchedulerLivenessMonitor livenessMonitor,
    ILogger<JobDispatcherPollingService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "JobDispatcherPollingService starting with polling interval {Interval}",
            configuration.JobDispatcherPollingInterval
        );

        using var timer = new PeriodicTimer(configuration.JobDispatcherPollingInterval);

        await RunJobDispatcher(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunJobDispatcher(stoppingToken);
        }

        logger.LogInformation("JobDispatcherPollingService stopping");
    }

    private async Task RunJobDispatcher(CancellationToken cancellationToken)
    {
        if (!configuration.JobDispatcherEnabled)
        {
            logger.LogDebug("JobDispatcher is disabled, skipping polling cycle");
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var train = scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

            logger.LogDebug("JobDispatcher polling cycle starting");
            await train.Run(Unit.Default, cancellationToken);

            // Stamp liveness only on a successful cycle (a no-op poll still proves the loop
            // and DB round-trip work). A failed run leaves the timestamp stale so the health
            // check flips unhealthy.
            livenessMonitor.RecordDispatchCycle();
            logger.LogDebug("JobDispatcher polling cycle completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during JobDispatcher polling cycle");
        }
    }
}

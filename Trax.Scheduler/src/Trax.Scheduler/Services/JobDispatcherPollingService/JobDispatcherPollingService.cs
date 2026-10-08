using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.JobDispatcherPollingService;

/// <summary>
/// Background service that polls the work queue on a configurable interval
/// and dispatches queued jobs via <see cref="IJobDispatcherTrain"/>.
/// </summary>
/// <remarks>
/// A <see cref="DispatcherWake"/> signal ends the wait early, when the data provider reports
/// that work was queued (see <c>QueuedWorkListenerService</c>). The interval stays the fallback,
/// and the only clock for work no notice covers, such as an entry whose scheduled time comes due.
/// </remarks>
internal class JobDispatcherPollingService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    SchedulerLivenessMonitor livenessMonitor,
    DispatcherWake wake,
    ILogger<JobDispatcherPollingService> logger
) : BackgroundService
{
    /// <summary>
    /// The least time from the start of one cycle to the start of a cycle a wake begins. Wakes
    /// already coalesce, but a host queuing work without pause would otherwise keep the
    /// dispatcher cycling back to back.
    /// </summary>
    internal static readonly TimeSpan MinimumWakeGap = TimeSpan.FromMilliseconds(100);

    private readonly Stopwatch _sinceCycleStarted = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "JobDispatcherPollingService starting with polling interval {Interval}",
            configuration.JobDispatcherPollingInterval
        );

        await RunJobDispatcher(stoppingToken);

        while (await WaitForNextCycleAsync(stoppingToken))
        {
            await RunJobDispatcher(stoppingToken);
        }

        logger.LogInformation("JobDispatcherPollingService stopping");
    }

    /// <summary>
    /// Waits for the polling interval or a wake, whichever comes first. Returns false when the host
    /// is stopping.
    /// </summary>
    private async Task<bool> WaitForNextCycleAsync(CancellationToken stoppingToken)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // The interval is read as the wait goes, so a runtime change applies to this wait.
        var timer = PollingDelay.WaitAsync(
            () => configuration.JobDispatcherPollingInterval,
            race.Token
        );
        var woken = wake.WaitAsync(race.Token);

        var first = await Task.WhenAny(timer, woken);
        await race.CancelAsync();
        await timer;
        try
        {
            await woken;
        }
        catch (OperationCanceledException) { }

        if (stoppingToken.IsCancellationRequested)
            return false;

        if (first == woken)
        {
            var gap = MinimumWakeGap - _sinceCycleStarted.Elapsed;
            if (gap > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(gap, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        // The cycle about to run covers every wake signalled so far, whichever ended the wait.
        wake.Consume();
        return true;
    }

    private async Task RunJobDispatcher(CancellationToken cancellationToken)
    {
        _sinceCycleStarted.Restart();

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

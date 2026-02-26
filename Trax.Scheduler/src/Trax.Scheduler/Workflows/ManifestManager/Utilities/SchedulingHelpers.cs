namespace Trax.Scheduler.Workflows.ManifestManager.Utilities;

using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Microsoft.Extensions.Logging;

/// <summary>
/// Helper utilities for scheduling logic in DetermineJobsToQueueStep.
/// </summary>
internal static class SchedulingHelpers
{
    /// <summary>
    /// Determines if a manifest should run at this moment based on its schedule type.
    /// </summary>
    /// <param name="manifest">The manifest to evaluate</param>
    /// <param name="now">The current time</param>
    /// <param name="logger">Logger for warnings and errors</param>
    /// <returns>True if the manifest should run now, false otherwise</returns>
    public static bool ShouldRunNow(Manifest manifest, DateTime now, ILogger logger)
    {
        return manifest.ScheduleType switch
        {
            ScheduleType.Cron => ShouldRunByCron(manifest, now, logger),
            ScheduleType.Interval => ShouldRunByInterval(manifest, now, logger),
            ScheduleType.OnDemand => false, // OnDemand manifests are never auto-scheduled, only via BulkEnqueueAsync
            ScheduleType.Dependent => false, // Dependent manifests are evaluated separately in DetermineJobsToQueueStep
            _ => false
        };
    }

    /// <summary>
    /// Checks if a cron-based manifest is due to run.
    /// </summary>
    private static bool ShouldRunByCron(Manifest manifest, DateTime now, ILogger logger)
    {
        if (string.IsNullOrEmpty(manifest.CronExpression))
        {
            logger.LogWarning(
                "Manifest {ManifestId} has ScheduleType=Cron but no cron_expression defined",
                manifest.Id
            );
            return false;
        }

        try
        {
            return IsTimeForCron(manifest.LastSuccessfulRun, manifest.CronExpression, now);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error evaluating cron expression for manifest {ManifestId}: {Expression}",
                manifest.Id,
                manifest.CronExpression
            );
            return false;
        }
    }

    /// <summary>
    /// Checks if an interval-based manifest is due to run.
    /// </summary>
    private static bool ShouldRunByInterval(Manifest manifest, DateTime now, ILogger logger)
    {
        if (!manifest.IntervalSeconds.HasValue || manifest.IntervalSeconds <= 0)
        {
            logger.LogWarning(
                "Manifest {ManifestId} has ScheduleType=Interval but no valid interval_seconds defined",
                manifest.Id
            );
            return false;
        }

        return IsTimeForInterval(manifest.LastSuccessfulRun, manifest.IntervalSeconds.Value, now);
    }

    /// <summary>
    /// Determines if a cron-based schedule is due to run at the current time.
    /// </summary>
    /// <param name="lastSuccessfulRun">The last time this manifest successfully ran, or null if never</param>
    /// <param name="cronExpression">The cron expression defining the schedule</param>
    /// <param name="now">The current time to evaluate against</param>
    /// <returns>True if the schedule is due to run, false otherwise</returns>
    /// <remarks>
    /// Currently implements a simplified version that checks if a cron pattern is due.
    /// For more complex cron parsing, consider integrating the Cronos library.
    ///
    /// This basic implementation evaluates:
    /// - If lastSuccessfulRun is null, returns true (job has never run)
    /// - Otherwise checks if enough time has passed based on cron pattern hints
    ///
    /// Common cron expressions:
    /// - "* * * * *" = every minute
    /// - "0 * * * *" = every hour
    /// - "0 0 * * *" = daily
    /// - "0 0 * * 0" = weekly
    /// - "0 0 1 * *" = monthly
    /// </remarks>
    public static bool IsTimeForCron(
        DateTime? lastSuccessfulRun,
        string cronExpression,
        DateTime now
    )
    {
        // If never run, always due
        if (lastSuccessfulRun is null)
            return true;

        // Parse the cron expression to extract frequency hint
        var parts = cronExpression.Split(' ');
        if (parts.Length < 5)
            return false;

        // Simple heuristic: parse common cron patterns to determine frequency
        var minute = parts[0];
        var hour = parts[1];
        var dayOfMonth = parts[2];

        // If minute is *, job runs every minute
        if (minute == "*")
            return (now - lastSuccessfulRun.Value).TotalSeconds >= 60;

        // If hour is * and minute is specific, it runs hourly
        if (hour == "*" && minute != "*")
            return (now - lastSuccessfulRun.Value).TotalSeconds >= 3600;

        // If dayOfMonth is * and hour is specific, it runs daily
        if (dayOfMonth == "*" && hour != "*")
            return (now - lastSuccessfulRun.Value).TotalSeconds >= 86400;

        // Default: check if at least a day has passed for safety
        return (now - lastSuccessfulRun.Value).TotalSeconds >= 86400;
    }

    /// <summary>
    /// Determines if an interval-based schedule is due to run at the current time.
    /// </summary>
    /// <param name="lastSuccessfulRun">The last time this manifest successfully ran, or null if never</param>
    /// <param name="intervalSeconds">The interval in seconds between runs</param>
    /// <param name="now">The current time to evaluate against</param>
    /// <returns>True if the interval has elapsed, false otherwise</returns>
    /// <remarks>
    /// If the manifest has never run, it is immediately eligible for execution.
    ///
    /// Example: If interval is 300 seconds (5 minutes) and last ran at 2024-01-10 12:00:00,
    /// and now it's 2024-01-10 12:05:30, the job is due to run.
    /// </remarks>
    public static bool IsTimeForInterval(
        DateTime? lastSuccessfulRun,
        int intervalSeconds,
        DateTime now
    )
    {
        if (lastSuccessfulRun is null)
        {
            // Never run before, so it's eligible now
            return true;
        }

        var nextScheduledTime = lastSuccessfulRun.Value.AddSeconds(intervalSeconds);
        return nextScheduledTime <= now;
    }
}

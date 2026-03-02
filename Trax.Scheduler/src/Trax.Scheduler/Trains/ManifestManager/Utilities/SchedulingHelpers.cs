namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;

/// <summary>
/// Helper utilities for scheduling logic in DetermineJobsToQueueStep.
/// </summary>
internal static class SchedulingHelpers
{
    /// <summary>
    /// Determines if a manifest should run at this moment based on its schedule type
    /// and misfire policy.
    /// </summary>
    /// <param name="manifest">The manifest to evaluate</param>
    /// <param name="now">The current time</param>
    /// <param name="config">Scheduler configuration for resolving global defaults</param>
    /// <param name="logger">Logger for warnings and errors</param>
    /// <returns>True if the manifest should run now, false otherwise</returns>
    public static bool ShouldRunNow(
        Manifest manifest,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger
    )
    {
        return manifest.ScheduleType switch
        {
            ScheduleType.Cron => ShouldRunByCron(manifest, now, config, logger),
            ScheduleType.Interval => ShouldRunByInterval(manifest, now, config, logger),
            ScheduleType.OnDemand => false, // OnDemand manifests are never auto-scheduled, only via BulkEnqueueAsync
            ScheduleType.Dependent => false, // Dependent manifests are evaluated separately in DetermineJobsToQueueStep
            _ => false,
        };
    }

    /// <summary>
    /// Checks if a cron-based manifest is due to run.
    /// </summary>
    private static bool ShouldRunByCron(
        Manifest manifest,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger
    )
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
            return EvaluateCronSchedule(manifest, now, config, logger);
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
    private static bool ShouldRunByInterval(
        Manifest manifest,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger
    )
    {
        if (!manifest.IntervalSeconds.HasValue || manifest.IntervalSeconds <= 0)
        {
            logger.LogWarning(
                "Manifest {ManifestId} has ScheduleType=Interval but no valid interval_seconds defined",
                manifest.Id
            );
            return false;
        }

        return EvaluateIntervalSchedule(manifest, now, config, logger);
    }

    /// <summary>
    /// Evaluates an interval-based schedule with misfire policy support.
    /// </summary>
    private static bool EvaluateIntervalSchedule(
        Manifest manifest,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger
    )
    {
        var intervalSeconds = manifest.IntervalSeconds!.Value;

        // If never run, always fire immediately
        if (manifest.LastSuccessfulRun is null)
            return true;

        var scheduledTime = manifest.LastSuccessfulRun.Value.AddSeconds(intervalSeconds);

        // Not yet due
        if (scheduledTime > now)
            return false;

        // Resolve effective misfire policy and threshold
        var policy = manifest.MisfirePolicy;
        var thresholdSeconds =
            manifest.MisfireThresholdSeconds ?? (int)config.DefaultMisfireThreshold.TotalSeconds;

        var overdueSeconds = (now - scheduledTime).TotalSeconds;

        // Within threshold: fire normally regardless of policy
        if (overdueSeconds <= thresholdSeconds)
            return true;

        // Beyond threshold: apply misfire policy
        if (policy == MisfirePolicy.FireOnceNow)
            return true;

        // DoNothing: advance to the most recent interval boundary and check threshold
        return EvaluateBoundary(
            manifest.LastSuccessfulRun.Value,
            intervalSeconds,
            now,
            thresholdSeconds,
            manifest.Id,
            overdueSeconds,
            "interval",
            logger
        );
    }

    /// <summary>
    /// Evaluates a cron-based schedule with misfire policy support.
    /// Uses the estimated cron frequency as a pseudo-interval for boundary math.
    /// </summary>
    private static bool EvaluateCronSchedule(
        Manifest manifest,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger
    )
    {
        // If never run, always due
        if (manifest.LastSuccessfulRun is null)
            return true;

        var estimatedFrequency = EstimateCronFrequency(manifest.CronExpression!);
        if (estimatedFrequency <= 0)
            return false;

        var elapsed = (now - manifest.LastSuccessfulRun.Value).TotalSeconds;

        // Not yet due
        if (elapsed < estimatedFrequency)
            return false;

        // Resolve effective misfire policy and threshold
        var policy = manifest.MisfirePolicy;
        var thresholdSeconds =
            manifest.MisfireThresholdSeconds ?? (int)config.DefaultMisfireThreshold.TotalSeconds;

        var overdueSeconds = elapsed - estimatedFrequency;

        // Within threshold: fire normally
        if (overdueSeconds <= thresholdSeconds)
            return true;

        // Beyond threshold: apply policy
        if (policy == MisfirePolicy.FireOnceNow)
            return true;

        // DoNothing: use the estimated frequency as a pseudo-interval
        return EvaluateBoundary(
            manifest.LastSuccessfulRun.Value,
            estimatedFrequency,
            now,
            thresholdSeconds,
            manifest.Id,
            overdueSeconds,
            "cron",
            logger
        );
    }

    /// <summary>
    /// Evaluates whether the current time falls within the misfire threshold of the most
    /// recent schedule boundary. Used by DoNothing policy for both interval and cron types.
    /// </summary>
    /// <remarks>
    /// After a long outage, this finds the most recent boundary (interval tick or estimated
    /// cron occurrence) before now and checks if we're within threshold of it. If yes, fires.
    /// If no, waits for the next boundary.
    ///
    /// Example: interval=5min, threshold=60s, LastSuccessfulRun=10:00, now=13:02
    ///   missedPeriods = floor(182min / 5min) = 36
    ///   boundary = 10:00 + 36*5min = 13:00
    ///   sinceBoundary = 2min = 120s > 60s threshold → skip, wait for 13:05
    ///
    /// Example: same but now=13:00:30
    ///   boundary = 13:00, sinceBoundary = 30s ≤ 60s → fire
    /// </remarks>
    private static bool EvaluateBoundary(
        DateTime lastSuccessfulRun,
        double frequencySeconds,
        DateTime now,
        int thresholdSeconds,
        long manifestId,
        double overdueSeconds,
        string scheduleKind,
        ILogger logger
    )
    {
        var totalElapsed = (now - lastSuccessfulRun).TotalSeconds;
        var missedPeriods = (int)(totalElapsed / frequencySeconds);
        var mostRecentBoundary = lastSuccessfulRun.AddSeconds(missedPeriods * frequencySeconds);
        var sinceBoundary = (now - mostRecentBoundary).TotalSeconds;

        if (sinceBoundary <= thresholdSeconds)
        {
            logger.LogDebug(
                "Manifest {ManifestId}: DoNothing {ScheduleKind} policy — within threshold of most recent boundary, firing",
                manifestId,
                scheduleKind
            );
            return true;
        }

        logger.LogInformation(
            "Manifest {ManifestId}: DoNothing {ScheduleKind} misfire policy — skipping overdue run "
                + "(overdue {Overdue:F0}s, threshold {Threshold}s, next boundary in {NextIn:F0}s)",
            manifestId,
            scheduleKind,
            overdueSeconds,
            thresholdSeconds,
            frequencySeconds - sinceBoundary
        );
        return false;
    }

    /// <summary>
    /// Estimates the frequency of a cron expression in seconds using a simplified heuristic.
    /// </summary>
    /// <remarks>
    /// This is used for misfire boundary calculations when a full cron library is not available.
    /// Precision will improve when the cron parser is upgraded to support 6-7 field expressions.
    /// </remarks>
    internal static int EstimateCronFrequency(string cronExpression)
    {
        var parts = cronExpression.Split(' ');
        if (parts.Length < 5)
            return 0;

        var minute = parts[0];
        var hour = parts[1];
        var dayOfMonth = parts[2];

        // If minute is *, job runs every minute
        if (minute == "*")
            return 60;

        // If hour is * and minute is specific, it runs hourly
        if (hour == "*" && minute != "*")
            return 3600;

        // If dayOfMonth is * and hour is specific, it runs daily
        if (dayOfMonth == "*" && hour != "*")
            return 86400;

        // Default: daily safety fallback
        return 86400;
    }

    /// <summary>
    /// Determines if a cron-based schedule is due to run at the current time.
    /// Preserved for backward compatibility with tests.
    /// </summary>
    public static bool IsTimeForCron(
        DateTime? lastSuccessfulRun,
        string cronExpression,
        DateTime now
    )
    {
        // If never run, always due
        if (lastSuccessfulRun is null)
            return true;

        var estimatedFrequency = EstimateCronFrequency(cronExpression);
        if (estimatedFrequency <= 0)
            return false;

        return (now - lastSuccessfulRun.Value).TotalSeconds >= estimatedFrequency;
    }

    /// <summary>
    /// Determines if an interval-based schedule is due to run at the current time.
    /// Preserved for backward compatibility with tests.
    /// </summary>
    public static bool IsTimeForInterval(
        DateTime? lastSuccessfulRun,
        int intervalSeconds,
        DateTime now
    )
    {
        if (lastSuccessfulRun is null)
            return true;

        var nextScheduledTime = lastSuccessfulRun.Value.AddSeconds(intervalSeconds);
        return nextScheduledTime <= now;
    }
}

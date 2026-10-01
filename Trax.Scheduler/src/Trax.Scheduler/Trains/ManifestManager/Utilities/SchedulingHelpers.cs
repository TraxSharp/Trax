namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;

/// <summary>
/// Helper utilities for scheduling logic in DetermineJobsToQueueJunction.
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
    /// <param name="lastCancelledRun">
    /// When the manifest's most recent cancelled run ended, if it has one. A cancelled run (a
    /// timeout or an operator's cancel) consumes the occurrence it ran for, so when it is later
    /// than <see cref="Manifest.LastSuccessfulRun"/> the schedule is evaluated from it instead.
    /// </param>
    /// <returns>True if the manifest should run now, false otherwise</returns>
    public static bool ShouldRunNow(
        Manifest manifest,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger,
        DateTime? lastCancelledRun = null
    )
    {
        // Check exclusions first — if the current time falls within any exclusion,
        // skip this manifest. Excluded periods are "intentionally skipped", not misfires.
        if (IsExcluded(manifest, now, logger))
            return false;

        var anchor = ScheduleAnchor.For(manifest, lastCancelledRun);

        return manifest.ScheduleType switch
        {
            ScheduleType.Cron => ShouldRunByCron(manifest, anchor, now, config, logger),
            ScheduleType.Interval => ShouldRunByInterval(manifest, anchor, now, config, logger),
            ScheduleType.Once => ShouldRunOnce(manifest, lastCancelledRun, now, logger),
            ScheduleType.OnDemand => false, // OnDemand manifests are never auto-scheduled, only via BulkEnqueueAsync
            ScheduleType.Dependent => false, // Dependent manifests are evaluated separately in DetermineJobsToQueueJunction
            _ => false,
        };
    }

    /// <summary>
    /// The run a schedule is evaluated from: the manifest's last successful run, or its last
    /// cancelled run when that is later. <see cref="NextScheduledRun"/> is the manifest's
    /// pre-computed next run, which was computed from the last success and so is dropped when a
    /// later cancelled run is the anchor.
    /// </summary>
    private readonly record struct ScheduleAnchor(DateTime? LastRun, DateTime? NextScheduledRun)
    {
        public static ScheduleAnchor For(Manifest manifest, DateTime? lastCancelledRun) =>
            lastCancelledRun is { } cancelled
            && (manifest.LastSuccessfulRun is null || cancelled > manifest.LastSuccessfulRun)
                ? new ScheduleAnchor(cancelled, null)
                : new ScheduleAnchor(manifest.LastSuccessfulRun, manifest.NextScheduledRun);
    }

    /// <summary>
    /// Checks if a cron-based manifest is due to run.
    /// </summary>
    private static bool ShouldRunByCron(
        Manifest manifest,
        ScheduleAnchor anchor,
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
            return EvaluateCronSchedule(manifest, anchor, now, config, logger);
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
        ScheduleAnchor anchor,
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

        return EvaluateIntervalSchedule(manifest, anchor, now, config, logger);
    }

    /// <summary>
    /// Checks if a one-off manifest is due to run: ScheduledAt &lt;= now, never successfully run,
    /// and not cancelled since it came due.
    /// </summary>
    private static bool ShouldRunOnce(
        Manifest manifest,
        DateTime? lastCancelledRun,
        DateTime now,
        ILogger logger
    )
    {
        // Already ran successfully — should have been auto-disabled, but guard anyway
        if (manifest.LastSuccessfulRun is not null)
        {
            logger.LogTrace(
                "Manifest {ManifestId} has ScheduleType=Once but already has LastSuccessfulRun, skipping",
                manifest.Id
            );
            return false;
        }

        if (manifest.ScheduledAt is null)
        {
            logger.LogWarning(
                "Manifest {ManifestId} has ScheduleType=Once but no scheduled_at defined",
                manifest.Id
            );
            return false;
        }

        // A run cancelled after the manifest came due consumed its one occurrence.
        if (lastCancelledRun is { } cancelled && cancelled >= manifest.ScheduledAt.Value)
        {
            logger.LogTrace(
                "Manifest {ManifestId} has ScheduleType=Once and its run was cancelled, skipping",
                manifest.Id
            );
            return false;
        }

        return manifest.ScheduledAt.Value <= now;
    }

    /// <summary>
    /// Evaluates an interval-based schedule with misfire policy support.
    /// </summary>
    private static bool EvaluateIntervalSchedule(
        Manifest manifest,
        ScheduleAnchor anchor,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger
    )
    {
        var intervalSeconds = manifest.IntervalSeconds!.Value;

        // If never run, always fire immediately
        if (anchor.LastRun is not { } lastRun)
            return true;

        // Use pre-computed next run time if available (variance-aware),
        // otherwise fall back to deterministic calculation.
        var scheduledTime = anchor.NextScheduledRun ?? lastRun.AddSeconds(intervalSeconds);

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
            lastRun,
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
    /// Uses Cronos for precise next-occurrence calculation, in UTC.
    /// </summary>
    /// <remarks>
    /// A cron that has never succeeded is due at <see cref="Manifest.NextScheduledRun"/>, which
    /// scheduling it set to its first occurrence. One with neither value, written before that
    /// was stamped, is due at once, unless its expression has no occurrence at all (one stored
    /// without passing <see cref="Schedule.FromCron"/>), which is never due. A cancelled run later than the last success replaces it as
    /// the anchor (see <see cref="ScheduleAnchor"/>), and then the next occurrence after it is due.
    /// </remarks>
    private static bool EvaluateCronSchedule(
        Manifest manifest,
        ScheduleAnchor anchor,
        DateTime now,
        SchedulerConfiguration config,
        ILogger logger
    )
    {
        // Never run (no success and no cancelled run) and no first occurrence recorded: due at
        // once, unless the expression can never fire. Scheduling records no first occurrence for
        // one of those either, and it must not run once and then never again.
        if (anchor.LastRun is null && anchor.NextScheduledRun is null)
            return CronParser
                .TryParse(manifest.CronExpression!)
                ?.GetNextOccurrence(now, TimeZoneInfo.Utc)
                is not null;

        // Use pre-computed next run time if available (variance-aware, or the first occurrence)
        DateTime nextDueValue;
        if (anchor.NextScheduledRun.HasValue)
        {
            nextDueValue = anchor.NextScheduledRun.Value;
        }
        else
        {
            var parsed = CronParser.TryParse(manifest.CronExpression!);
            if (parsed is null)
            {
                logger.LogWarning(
                    "Manifest {ManifestId}: could not parse cron expression '{Expression}'",
                    manifest.Id,
                    manifest.CronExpression
                );
                return false;
            }

            var nextDue = parsed.GetNextOccurrence(anchor.LastRun!.Value, TimeZoneInfo.Utc);
            if (nextDue is null)
                return false;

            nextDueValue = nextDue.Value;
        }

        // Not yet due
        if (nextDueValue > now)
            return false;

        // Resolve effective misfire policy and threshold
        var policy = manifest.MisfirePolicy;
        var thresholdSeconds =
            manifest.MisfireThresholdSeconds ?? (int)config.DefaultMisfireThreshold.TotalSeconds;

        var overdueSeconds = (now - nextDueValue).TotalSeconds;

        // Within threshold: fire normally
        if (overdueSeconds <= thresholdSeconds)
            return true;

        // Beyond threshold: apply policy
        if (policy == MisfirePolicy.FireOnceNow)
            return true;

        // DoNothing: find the most recent cron occurrence before now and check threshold.
        // When NextScheduledRun was used, we need to re-parse the cron expression for boundary evaluation.
        var cronParsed = CronParser.TryParse(manifest.CronExpression!);
        if (cronParsed is null)
            return false;

        // Occurrences after the last run (success, or a later cancelled run) count; a cron that
        // never ran counts from its first occurrence, inclusive.
        var countFrom = anchor.LastRun ?? nextDueValue.AddTicks(-1);

        return EvaluateCronBoundary(
            cronParsed,
            manifest,
            countFrom,
            now,
            thresholdSeconds,
            overdueSeconds,
            logger
        );
    }

    /// <summary>
    /// For DoNothing misfire policy on cron schedules: finds the most recent cron occurrence
    /// at or before now (see <see cref="LatestOccurrence"/>) and checks if we're within threshold
    /// of it.
    /// </summary>
    private static bool EvaluateCronBoundary(
        Cronos.CronExpression parsed,
        Manifest manifest,
        DateTime countFrom,
        DateTime now,
        int thresholdSeconds,
        double overdueSeconds,
        ILogger logger
    )
    {
        var mostRecent = LatestOccurrence(parsed, countFrom, now);

        if (mostRecent is null)
            return false;

        var sinceBoundary = (now - mostRecent.Value).TotalSeconds;

        if (sinceBoundary <= thresholdSeconds)
        {
            logger.LogDebug(
                "Manifest {ManifestId}: DoNothing cron policy — within threshold of most recent boundary, firing",
                manifest.Id
            );
            return true;
        }

        var nextOccurrence = parsed.GetNextOccurrence(now, TimeZoneInfo.Utc);
        var nextIn = nextOccurrence.HasValue ? (nextOccurrence.Value - now).TotalSeconds : 0;

        logger.LogInformation(
            "Manifest {ManifestId}: DoNothing cron misfire policy — skipping overdue run "
                + "(overdue {Overdue:F0}s, threshold {Threshold}s, next boundary in {NextIn:F0}s)",
            manifest.Id,
            overdueSeconds,
            thresholdSeconds,
            nextIn
        );
        return false;
    }

    /// <summary>
    /// Finds the latest occurrence of <paramref name="parsed"/> that is later than
    /// <paramref name="after"/> and no later than <paramref name="now"/>, evaluated in UTC.
    /// </summary>
    /// <remarks>
    /// Cronos only searches forward, so this bisects on the instant a forward search starts from:
    /// an inclusive search from any instant up to the latest occurrence lands at or before
    /// <paramref name="now"/>, and one from any later instant lands after it. That takes about
    /// fifty cron evaluations whatever the gap, where walking forward one occurrence at a time
    /// took one per missed occurrence: a year of a minutely cron is half a million.
    /// </remarks>
    /// <returns>The latest occurrence in <c>(after, now]</c>, or null when there is none.</returns>
    internal static DateTime? LatestOccurrence(
        Cronos.CronExpression parsed,
        DateTime after,
        DateTime now
    )
    {
        var first = parsed.GetNextOccurrence(after, TimeZoneInfo.Utc);
        if (first is null || first.Value > now)
            return null;

        // lo always starts a search that lands at or before now; hi never does.
        var lo = first.Value.Ticks;
        var hi = now.Ticks + 1;
        while (hi - lo > 1)
        {
            var mid = lo + (hi - lo) / 2;
            var next = parsed.GetNextOccurrence(
                new DateTime(mid, DateTimeKind.Utc),
                TimeZoneInfo.Utc,
                inclusive: true
            );
            if (next is not null && next.Value <= now)
                lo = mid;
            else
                hi = mid;
        }

        return parsed.GetNextOccurrence(
            new DateTime(lo, DateTimeKind.Utc),
            TimeZoneInfo.Utc,
            inclusive: true
        );
    }

    /// <summary>
    /// Evaluates whether the current time falls within the misfire threshold of the most
    /// recent schedule boundary. Used by DoNothing policy for interval-based schedules.
    /// </summary>
    /// <remarks>
    /// After a long outage, this finds the most recent boundary (interval tick)
    /// before now and checks if we're within threshold of it. If yes, fires.
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
    /// Determines if a cron-based schedule is due to run at the current time.
    /// Supports both 5-field and 6-field (with seconds) cron expressions.
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

        var nextDue = CronParser.GetNextOccurrence(cronExpression, lastSuccessfulRun.Value);
        if (nextDue is null)
            return false;

        return nextDue.Value <= now;
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

    /// <summary>
    /// Computes the next scheduled run time with variance (jitter) applied.
    /// Called after each successful execution to pre-compute a deterministic next-run time.
    /// </summary>
    /// <param name="manifest">The manifest that just completed successfully</param>
    /// <returns>
    /// The next scheduled run time with jitter applied, or null if variance is not configured
    /// or the schedule type doesn't support variance.
    /// </returns>
    internal static DateTime? ComputeNextScheduledRun(Manifest manifest)
    {
        if (manifest.VarianceSeconds is null or <= 0)
            return null;

        if (manifest.LastSuccessfulRun is null)
            return null;

        var lastRun = manifest.LastSuccessfulRun.Value;
        DateTime baseNextRun;

        if (manifest.ScheduleType == ScheduleType.Interval && manifest.IntervalSeconds is > 0)
        {
            baseNextRun = lastRun.AddSeconds(manifest.IntervalSeconds.Value);
        }
        else if (
            manifest.ScheduleType == ScheduleType.Cron
            && !string.IsNullOrEmpty(manifest.CronExpression)
        )
        {
            var parsed = CronParser.TryParse(manifest.CronExpression);
            var next = parsed?.GetNextOccurrence(lastRun, TimeZoneInfo.Utc);
            if (next is null)
                return null;
            baseNextRun = next.Value;
        }
        else
        {
            return null;
        }

        var jitterSeconds = Random.Shared.Next(0, manifest.VarianceSeconds.Value + 1);
        return baseNextRun.AddSeconds(jitterSeconds);
    }

    /// <summary>
    /// Checks whether the current time falls within any of the manifest's exclusion windows.
    /// </summary>
    private static bool IsExcluded(Manifest manifest, DateTime now, ILogger logger)
    {
        var exclusions = manifest.GetExclusions();
        if (exclusions.Count == 0)
            return false;

        foreach (var exclusion in exclusions)
        {
            if (exclusion.IsExcluded(now))
            {
                logger.LogDebug(
                    "Manifest {ManifestId} is excluded by {ExclusionType} exclusion, skipping",
                    manifest.Id,
                    exclusion.Type
                );
                return true;
            }
        }

        return false;
    }
}

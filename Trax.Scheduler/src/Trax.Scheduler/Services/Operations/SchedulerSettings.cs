using Trax.Core.Extensions;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.Operations;

/// <summary>The persisted scheduler settings, in the order the row lists them.</summary>
internal static class SchedulerSettings
{
    private static (bool, T) Set<T>(T value) => (true, value);

    private static (bool, T) Patch<T>(T? value)
        where T : struct => value is { } v ? (true, v) : (false, default);

    private static string? Check<T>(Func<T?, string, string?> limit, T value, string name)
        where T : struct => limit(value, name);

    /// <summary>Every persisted setting.</summary>
    public static readonly IReadOnlyList<ISchedulerSetting> All =
    [
        new SchedulerSetting<bool>(
            nameof(SchedulerConfig.ManifestManagerEnabled),
            r => Set(r.ManifestManagerEnabled),
            (r, v) => r.ManifestManagerEnabled = v,
            t => t.Configuration.ManifestManagerEnabled,
            (t, v) => t.Configuration.ManifestManagerEnabled = v,
            i => Patch(i.ManifestManagerEnabled)
        ),
        new SchedulerSetting<bool>(
            nameof(SchedulerConfig.JobDispatcherEnabled),
            r => Set(r.JobDispatcherEnabled),
            (r, v) => r.JobDispatcherEnabled = v,
            t => t.Configuration.JobDispatcherEnabled,
            (t, v) => t.Configuration.JobDispatcherEnabled = v,
            i => Patch(i.JobDispatcherEnabled)
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.ManifestManagerPollingInterval),
            r => Set(r.ManifestManagerPollingInterval),
            (r, v) => r.ManifestManagerPollingInterval = v,
            t => t.Configuration.ManifestManagerPollingInterval,
            (t, v) => t.Configuration.ManifestManagerPollingInterval = v,
            i => Patch(i.ManifestManagerPollingInterval),
            (v, n) => Check(SchedulerConfigLimits.TimerInterval, v, n)
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.JobDispatcherPollingInterval),
            r => Set(r.JobDispatcherPollingInterval),
            (r, v) => r.JobDispatcherPollingInterval = v,
            t => t.Configuration.JobDispatcherPollingInterval,
            (t, v) => t.Configuration.JobDispatcherPollingInterval = v,
            i => Patch(i.JobDispatcherPollingInterval),
            (v, n) => Check(SchedulerConfigLimits.TimerInterval, v, n)
        ),
        // Null is a value here: no limit. ClearMaxActiveJobs is how a patch sets it.
        new SchedulerSetting<int?>(
            nameof(SchedulerConfig.MaxActiveJobs),
            r => Set(r.MaxActiveJobs),
            (r, v) => r.MaxActiveJobs = v,
            t => t.Configuration.MaxActiveJobs,
            (t, v) => t.Configuration.MaxActiveJobs = v,
            i =>
                i.ClearMaxActiveJobs ? (true, null)
                : i.MaxActiveJobs is { } v ? (true, v)
                : (false, null),
            SchedulerConfigLimits.AtLeastOne
        ),
        new SchedulerSetting<int>(
            nameof(SchedulerConfig.DefaultMaxRetries),
            r => Set(r.DefaultMaxRetries),
            (r, v) => r.DefaultMaxRetries = v,
            t => t.Configuration.DefaultMaxRetries,
            (t, v) => t.Configuration.DefaultMaxRetries = v,
            i => Patch(i.DefaultMaxRetries),
            (v, n) => Check(SchedulerConfigLimits.NotNegative, v, n)
        ),
        // No column: stored only in the row's overrides, so a row written before them never
        // sets it.
        new SchedulerSetting<TimeSpan>(
            nameof(UpdateSchedulerConfigInput.FailureCountWindow),
            readColumn: null,
            writeColumn: null,
            t => t.Configuration.FailureCountWindow,
            (t, v) => t.Configuration.FailureCountWindow = v,
            i => Patch(i.FailureCountWindow),
            (v, n) => Check(SchedulerConfigLimits.PositiveDuration, v, n)
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.DefaultRetryDelay),
            r => Set(r.DefaultRetryDelay),
            (r, v) => r.DefaultRetryDelay = v,
            t => t.Configuration.DefaultRetryDelay,
            (t, v) => t.Configuration.DefaultRetryDelay = v,
            i => Patch(i.DefaultRetryDelay),
            (v, n) => Check(SchedulerConfigLimits.NonNegativeDuration, v, n)
        ),
        new SchedulerSetting<double>(
            nameof(SchedulerConfig.RetryBackoffMultiplier),
            r => Set(r.RetryBackoffMultiplier),
            (r, v) => r.RetryBackoffMultiplier = v,
            t => t.Configuration.RetryBackoffMultiplier,
            (t, v) => t.Configuration.RetryBackoffMultiplier = v,
            i => Patch(i.RetryBackoffMultiplier),
            (v, n) => Check(SchedulerConfigLimits.BackoffMultiplier, v, n)
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.MaxRetryDelay),
            r => Set(r.MaxRetryDelay),
            (r, v) => r.MaxRetryDelay = v,
            t => t.Configuration.MaxRetryDelay,
            (t, v) => t.Configuration.MaxRetryDelay = v,
            i => Patch(i.MaxRetryDelay),
            (v, n) => Check(SchedulerConfigLimits.NonNegativeDuration, v, n)
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.DefaultJobTimeout),
            r => Set(r.DefaultJobTimeout),
            (r, v) => r.DefaultJobTimeout = v,
            t => t.Configuration.DefaultJobTimeout,
            (t, v) => t.Configuration.DefaultJobTimeout = v,
            i => Patch(i.DefaultJobTimeout),
            (v, n) => Check(SchedulerConfigLimits.PositiveDuration, v, n)
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.StalePendingTimeout),
            r => Set(r.StalePendingTimeout),
            (r, v) => r.StalePendingTimeout = v,
            t => t.Configuration.StalePendingTimeout,
            (t, v) => t.Configuration.StalePendingTimeout = v,
            i => Patch(i.StalePendingTimeout),
            (v, n) => Check(SchedulerConfigLimits.PositiveDuration, v, n)
        ),
        new SchedulerSetting<bool>(
            nameof(SchedulerConfig.RecoverStuckJobsOnStartup),
            r => Set(r.RecoverStuckJobsOnStartup),
            (r, v) => r.RecoverStuckJobsOnStartup = v,
            t => t.Configuration.RecoverStuckJobsOnStartup,
            (t, v) => t.Configuration.RecoverStuckJobsOnStartup = v,
            i => Patch(i.RecoverStuckJobsOnStartup)
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.DeadLetterRetentionPeriod),
            r => Set(r.DeadLetterRetentionPeriod),
            (r, v) => r.DeadLetterRetentionPeriod = v,
            t => t.Configuration.DeadLetterRetentionPeriod,
            // Fails closed: a retention stated in code is a floor a saved one cannot go under.
            (t, v) =>
                t.Configuration.DeadLetterRetentionPeriod =
                    t.Configuration.ConfiguredDeadLetterRetentionPeriod is { } coded && coded > v
                        ? coded
                        : v,
            i => Patch(i.DeadLetterRetentionPeriod),
            (v, n) => Check(SchedulerConfigLimits.NonNegativeDuration, v, n)
        ),
        new SchedulerSetting<bool>(
            nameof(SchedulerConfig.AutoPurgeDeadLetters),
            r => Set(r.AutoPurgeDeadLetters),
            (r, v) => r.AutoPurgeDeadLetters = v,
            t => t.Configuration.AutoPurgeDeadLetters,
            // Fails closed: a purge turned off in code stays off whatever is saved.
            (t, v) =>
                t.Configuration.AutoPurgeDeadLetters =
                    v && t.Configuration.ConfiguredAutoPurgeDeadLetters != false,
            i => Patch(i.AutoPurgeDeadLetters)
        ),
        // A null column leaves the host's configured count; ClearLocalWorkerCount resets the
        // count to the processor count.
        new SchedulerSetting<int>(
            nameof(SchedulerConfig.LocalWorkerCount),
            r => r.LocalWorkerCount is { } v ? (true, v) : (false, 0),
            (r, v) => r.LocalWorkerCount = v,
            t => t.Workers.NotNull().WorkerCount,
            (t, v) => t.Workers.NotNull().WorkerCount = v,
            i =>
                i.ClearLocalWorkerCount ? (true, Environment.ProcessorCount)
                : i.LocalWorkerCount is { } v ? (true, v)
                : (false, 0),
            (v, n) => Check(SchedulerConfigLimits.WorkerCount, v, n),
            t => t.Workers is not null
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.MetadataCleanupInterval),
            r => r.MetadataCleanupInterval is { } v ? (true, v) : (false, default),
            (r, v) => r.MetadataCleanupInterval = v,
            t => t.Configuration.MetadataCleanup.NotNull().CleanupInterval,
            (t, v) => t.Configuration.MetadataCleanup.NotNull().CleanupInterval = v,
            i => Patch(i.MetadataCleanupInterval),
            (v, n) => Check(SchedulerConfigLimits.TimerInterval, v, n),
            t => t.Configuration.MetadataCleanup is not null
        ),
        new SchedulerSetting<TimeSpan>(
            nameof(SchedulerConfig.MetadataCleanupRetention),
            r => r.MetadataCleanupRetention is { } v ? (true, v) : (false, default),
            (r, v) => r.MetadataCleanupRetention = v,
            t => t.Configuration.MetadataCleanup.NotNull().RetentionPeriod,
            (t, v) => t.Configuration.MetadataCleanup.NotNull().RetentionPeriod = v,
            i => Patch(i.MetadataCleanupRetention),
            (v, n) => Check(SchedulerConfigLimits.PositiveDuration, v, n),
            t => t.Configuration.MetadataCleanup is not null
        ),
    ];

    /// <summary>
    /// Turns a row written before <c>overrides</c> existed into one that names every setting its
    /// columns set, so the first save to it keeps every value an operator stored then rather than
    /// narrowing the row to the one setting the save names. A row that already has
    /// <c>overrides</c> is left alone.
    /// </summary>
    public static void UpgradeLegacyRow(SchedulerConfig row)
    {
        if (row.Overrides is not null)
            return;

        row.Overrides = "{}";
        foreach (var setting in All)
            if (setting.TryReadLegacyRow(row, out var value))
                setting.WriteRow(row, value);
    }

    /// <summary>
    /// What the host runs with now, for each setting it has: the baseline the live refresh
    /// returns a setting to when the stored row stops setting it.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Capture(SchedulerSettingsTarget target) =>
        All.Where(s => s.AppliesTo(target))
            .ToDictionary(s => s.Name, s => s.ReadLive(target), StringComparer.Ordinal);
}

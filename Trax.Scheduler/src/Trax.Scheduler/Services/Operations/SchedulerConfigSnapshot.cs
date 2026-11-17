namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Snapshot of the live scheduler runtime settings: the dashboard-editable subset of
/// <c>SchedulerConfiguration</c>, <c>LocalWorkerOptions</c>, and
/// <c>MetadataCleanupConfiguration</c>. Returned by
/// <see cref="IOperationsService.GetSchedulerConfig"/>; consumed by both the
/// dashboard's ServerSettingsPage and the GraphQL <c>operations.config.scheduler</c>
/// query.
/// </summary>
public record SchedulerConfigSnapshot(
    bool ManifestManagerEnabled,
    bool JobDispatcherEnabled,
    TimeSpan ManifestManagerPollingInterval,
    TimeSpan JobDispatcherPollingInterval,
    int? MaxActiveJobs,
    int DefaultMaxRetries,
    TimeSpan DefaultRetryDelay,
    double RetryBackoffMultiplier,
    TimeSpan MaxRetryDelay,
    TimeSpan DefaultJobTimeout,
    TimeSpan StalePendingTimeout,
    bool RecoverStuckJobsOnStartup,
    TimeSpan DeadLetterRetentionPeriod,
    bool AutoPurgeDeadLetters,
    int? LocalWorkerCount,
    TimeSpan? MetadataCleanupInterval,
    TimeSpan? MetadataCleanupRetention
)
{
    /// <summary>
    /// How far back a manifest's failed runs are counted toward its retry backoff and its dead
    /// letter (<c>SchedulerConfiguration.FailureCountWindow</c>).
    /// </summary>
    public TimeSpan FailureCountWindow { get; init; }
}

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Snapshot of the live scheduler runtime settings: the dashboard-editable subset of
/// <c>SchedulerConfiguration</c>, <c>LocalWorkerOptions</c>, and
/// <c>MetadataCleanupConfiguration</c>. Returned by
/// <see cref="IOperationsService.GetSchedulerConfigAsync"/>; consumed by both the
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
);

/// <summary>
/// Patch input for <see cref="IOperationsService.UpdateSchedulerConfigAsync"/>. Each
/// field is independent: <c>null</c> means "leave unchanged".
/// </summary>
/// <remarks>
/// To clear <see cref="MaxActiveJobs"/> or <see cref="LocalWorkerCount"/> (set them to
/// "no limit"), set the corresponding <c>Clear*</c> flag to <c>true</c>. Without that
/// flag, a <c>null</c> value means "no change" because <c>int?</c> can't distinguish
/// "unset" from "set to null" in a patch record.
/// </remarks>
public record UpdateSchedulerConfigInput(
    bool? ManifestManagerEnabled = null,
    bool? JobDispatcherEnabled = null,
    TimeSpan? ManifestManagerPollingInterval = null,
    TimeSpan? JobDispatcherPollingInterval = null,
    int? MaxActiveJobs = null,
    bool ClearMaxActiveJobs = false,
    int? DefaultMaxRetries = null,
    TimeSpan? DefaultRetryDelay = null,
    double? RetryBackoffMultiplier = null,
    TimeSpan? MaxRetryDelay = null,
    TimeSpan? DefaultJobTimeout = null,
    TimeSpan? StalePendingTimeout = null,
    bool? RecoverStuckJobsOnStartup = null,
    TimeSpan? DeadLetterRetentionPeriod = null,
    bool? AutoPurgeDeadLetters = null,
    int? LocalWorkerCount = null,
    bool ClearLocalWorkerCount = false,
    TimeSpan? MetadataCleanupInterval = null,
    TimeSpan? MetadataCleanupRetention = null
);

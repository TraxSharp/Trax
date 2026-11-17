namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Patch input for <see cref="IOperationsService.UpdateSchedulerConfigAsync"/>. Each
/// field is independent: <see langword="null"/> means "leave unchanged".
/// </summary>
/// <remarks>
/// To clear <see cref="MaxActiveJobs"/> or <see cref="LocalWorkerCount"/> (set them to
/// "no limit"), set the corresponding <c>Clear*</c> flag to <see langword="true"/>. Without that
/// flag, a <see langword="null"/> value means "no change" because <c>int?</c> can't distinguish
/// "unset" from "set to null" in a patch record.
/// <para>
/// A change applies to the running scheduler without a restart: the pollers read their interval
/// before each wait, and the other settings are read each time they are used. The exception is
/// <see cref="LocalWorkerCount"/>: the worker pool starts that many workers when the host starts,
/// so a new count is stored at once but applies after a restart.
/// </para>
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
)
{
    /// <summary>
    /// A new failure count window, or <see langword="null"/> to leave it unchanged. Must be between one
    /// second and ten years.
    /// </summary>
    public TimeSpan? FailureCountWindow { get; init; }
}

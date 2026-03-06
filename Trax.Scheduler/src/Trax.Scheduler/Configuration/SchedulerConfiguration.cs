using Trax.Effect.Enums;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for the Trax.Scheduler system.
/// </summary>
public class SchedulerConfiguration
{
    /// <summary>
    /// Gets the collection of pending manifests to be seeded on startup.
    /// </summary>
    /// <remarks>
    /// Pending manifests are added via the fluent configuration API
    /// (e.g., <c>.Schedule&lt;TTrain, TInput&gt;(...)</c>) and seeded
    /// automatically on startup by the ManifestPollingService.
    /// </remarks>
    internal List<PendingManifest> PendingManifests { get; } = [];

    /// <summary>
    /// Whether the ManifestManager train is enabled during polling cycles.
    /// </summary>
    /// <remarks>
    /// When disabled, the ManifestManager will not run during polling cycles, meaning
    /// no new work queue entries will be created from scheduled manifests. Existing
    /// work queue entries are not affected. Takes effect on the next polling cycle.
    /// </remarks>
    public bool ManifestManagerEnabled { get; set; } = true;

    /// <summary>
    /// Whether the JobDispatcher train is enabled during polling cycles.
    /// </summary>
    /// <remarks>
    /// When disabled, the JobDispatcher will not run during polling cycles, meaning
    /// no queued work will be dispatched to the job submitter. Work queue
    /// entries will continue to accumulate if the ManifestManager is still enabled.
    /// Takes effect on the next polling cycle.
    /// </remarks>
    public bool JobDispatcherEnabled { get; set; } = true;

    /// <summary>
    /// The interval at which the ManifestManagerPollingService polls for pending jobs.
    /// </summary>
    public TimeSpan ManifestManagerPollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The interval at which the JobDispatcherPollingService polls the work queue.
    /// </summary>
    public TimeSpan JobDispatcherPollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The maximum number of active jobs (Pending + InProgress Metadata) allowed across all manifests.
    /// </summary>
    /// <remarks>
    /// Enforced by the JobDispatcher at dispatch time. Metadata whose train name appears in
    /// <see cref="ExcludedTrainTypeNames"/> is excluded from the count. By default, internal
    /// scheduler trains (JobDispatcher, JobRunner, ManifestManager, MetadataCleanup)
    /// are excluded. When the total number of active jobs reaches this limit, the JobDispatcher
    /// will not dispatch new work queue entries until existing jobs complete.
    /// Work queue entries remain in Queued status as a buffer.
    /// Set to null to disable this limit (unlimited).
    /// </remarks>
    public int? MaxActiveJobs { get; set; } = 10;

    /// <summary>
    /// Priority boost automatically applied to dependent train work queue entries.
    /// </summary>
    /// <remarks>
    /// When the ManifestManager creates work queue entries for dependent manifests
    /// (ScheduleType.Dependent), this value is added to the manifest's base priority,
    /// clamped to [0, 31]. Set to 0 to disable the automatic boost.
    /// </remarks>
    public int DependentPriorityBoost { get; set; } = 16;

    /// <summary>
    /// Train type names excluded from the MaxActiveJobs count.
    /// </summary>
    /// <remarks>
    /// Metadata whose <c>Name</c> column matches any entry in this list is not counted
    /// toward <see cref="MaxActiveJobs"/>. Populated automatically with internal scheduler
    /// train types during <c>Build()</c>. Additional types can be added via
    /// <see cref="SchedulerConfigurationBuilder.ExcludeFromMaxActiveJobs{TTrain}"/>.
    /// </remarks>
    internal List<string> ExcludedTrainTypeNames { get; } = [];

    /// <summary>
    /// The default number of retry attempts before a job is dead-lettered.
    /// </summary>
    /// <remarks>
    /// This can be overridden per-manifest via ManifestScheduleProperties.
    /// </remarks>
    public int DefaultMaxRetries { get; set; } = 3;

    /// <summary>
    /// The default delay between retry attempts.
    /// </summary>
    /// <remarks>
    /// This can be combined with RetryBackoffMultiplier for exponential backoff.
    /// </remarks>
    public TimeSpan DefaultRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Multiplier applied to retry delay on each subsequent retry (exponential backoff).
    /// </summary>
    /// <remarks>
    /// Set to 1.0 for constant retry delay. Default of 2.0 means delays of 5m, 10m, 20m, etc.
    /// </remarks>
    public double RetryBackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum retry delay to prevent unbounded backoff growth.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Timeout after which a running job is considered stuck.
    /// </summary>
    /// <remarks>
    /// Jobs that have been in "InProgress" state longer than this duration
    /// may be automatically failed and potentially retried.
    /// </remarks>
    public TimeSpan DefaultJobTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Timeout after which a Pending job that was never picked up is automatically failed.
    /// </summary>
    /// <remarks>
    /// Acts as a safety net for dispatch failures — if a job remains in Pending state
    /// longer than this duration, the ManifestManager's ReapStalePendingMetadataStep
    /// will mark it as Failed. This catches cases where the job submitter failed to
    /// deliver the job (e.g. remote worker unreachable, Lambda timeout) and the
    /// immediate failure handling in DispatchJobsStep also failed.
    /// </remarks>
    public TimeSpan StalePendingTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// The default misfire policy applied to manifests that do not specify one.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="MisfirePolicy.FireOnceNow"/> for backward compatibility.
    /// Can be overridden per-manifest via the ScheduleOptions fluent API.
    /// </remarks>
    public MisfirePolicy DefaultMisfirePolicy { get; set; } = MisfirePolicy.FireOnceNow;

    /// <summary>
    /// The default misfire threshold — the grace period for determining when a missed
    /// run should trigger the misfire policy.
    /// </summary>
    /// <remarks>
    /// If a manifest is overdue by less than this threshold, it fires normally regardless
    /// of its <see cref="MisfirePolicy"/>. If overdue by more, the manifest's MisfirePolicy
    /// determines behavior. Can be overridden per-manifest via MisfireThresholdSeconds.
    /// </remarks>
    public TimeSpan DefaultMisfireThreshold { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether to automatically recover stuck jobs on scheduler startup.
    /// </summary>
    /// <remarks>
    /// If true, jobs that were "InProgress" when the system shut down will be
    /// re-evaluated on startup and potentially requeued.
    /// </remarks>
    public bool RecoverStuckJobsOnStartup { get; set; } = true;

    /// <summary>
    /// How long to retain dead letter records after resolution.
    /// </summary>
    public TimeSpan DeadLetterRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether to enable automatic purging of old dead letter records.
    /// </summary>
    public bool AutoPurgeDeadLetters { get; set; } = true;

    /// <summary>
    /// Configuration for automatic metadata cleanup, if enabled.
    /// </summary>
    /// <remarks>
    /// When set (via <c>.AddMetadataCleanup()</c>), a background service will
    /// periodically delete old metadata entries for the configured train types.
    /// Null means metadata cleanup is disabled.
    /// </remarks>
    public MetadataCleanupConfiguration? MetadataCleanup { get; set; }

    /// <summary>
    /// Whether to automatically prune manifests from the database that are no longer
    /// defined in the startup configuration.
    /// </summary>
    /// <remarks>
    /// When enabled, the startup service compares the set of ExternalIds defined via
    /// the fluent configuration API against all manifests in the database. Any manifest
    /// whose ExternalId is not in the configured set is deleted along with its related
    /// WorkQueue, DeadLetter, and Metadata records. This ensures that removing a schedule
    /// definition from code also removes it from the database on the next startup.
    /// Disable this if you create manifests dynamically at runtime via
    /// <see cref="Services.TraxScheduler.ITraxScheduler"/>.
    /// </remarks>
    public bool PruneOrphanedManifests { get; set; } = true;
}

using System.ComponentModel;
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
    /// (e.g., <c>.Schedule&lt;TTrain&gt;(...)</c>) and seeded
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
    public bool ManifestManagerEnabled
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = true;

    /// <summary>
    /// Whether the JobDispatcher train is enabled during polling cycles.
    /// </summary>
    /// <remarks>
    /// When disabled, the JobDispatcher will not run during polling cycles, meaning
    /// no queued work will be dispatched to the job submitter. Work queue
    /// entries will continue to accumulate if the ManifestManager is still enabled.
    /// Takes effect on the next polling cycle.
    /// </remarks>
    public bool JobDispatcherEnabled { get; internal set; } = true;

    /// <summary>
    /// The interval at which the ManifestManagerPollingService polls for pending jobs.
    /// </summary>
    public TimeSpan ManifestManagerPollingInterval
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The interval at which the JobDispatcherPollingService polls the work queue.
    /// </summary>
    public TimeSpan JobDispatcherPollingInterval
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the JobDispatcher may go without completing a polling cycle before the
    /// <c>AddTraxSchedulerLiveness()</c> health check reports unhealthy. When null, the
    /// check uses max(<see cref="JobDispatcherPollingInterval"/> * 10, 30s).
    /// </summary>
    public TimeSpan? SchedulerLivenessThreshold { get; internal set; }

    /// <summary>
    /// The maximum number of work queue entries dispatched concurrently within a single
    /// JobDispatcher polling cycle.
    /// </summary>
    /// <remarks>
    /// Controls how many entries <see cref="Trains.JobDispatcher.Junctions.DispatchJobsJunction"/>
    /// processes in parallel. Useful when using <c>UseRemoteWorkers()</c> where each dispatch
    /// is an HTTP POST that blocks until the remote endpoint completes — without this, entries
    /// are dispatched sequentially, and cycle duration scales linearly with entry count.
    /// For local workers (<c>PostgresJobSubmitter</c>), this has minimal impact since database
    /// INSERT returns in microseconds.
    /// Set to 1 for sequential dispatch (default, backward compatible).
    /// </remarks>
    public int MaxConcurrentDispatch { get; internal set; } = 1;

    /// <summary>
    /// The maximum number of active jobs (Pending + InProgress Metadata) allowed across all manifests.
    /// </summary>
    /// <remarks>
    /// Enforced by the JobDispatcher at dispatch time. Metadata whose train name appears in
    /// <see cref="ExcludedTrainTypeNames"/> is excluded from the count. By default, internal
    /// scheduler trains (JobDispatcher, JobRunner, ManifestManager, MetadataCleanup,
    /// DeadLetterCleanup) are excluded. When the total number of active jobs reaches this limit,
    /// the JobDispatcher will not dispatch new work queue entries until existing jobs complete.
    /// Work queue entries remain in Queued status as a buffer.
    /// Set to null to disable this limit (unlimited).
    ///
    /// <para>The limit is approximate, not a hard ceiling. Each dispatcher counts active jobs at
    /// the start of its cycle without a lock shared with other hosts, so with N hosts dispatching
    /// against the same database the number of active jobs can reach N times the limit. The same
    /// holds for a group's own limit.</para>
    /// </remarks>
    public int? MaxActiveJobs
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = 10;

    /// <summary>
    /// The maximum number of dispatch attempts before a work queue entry is permanently failed.
    /// </summary>
    /// <remarks>
    /// When the job submitter fails (e.g., remote worker throttling or unavailability) and
    /// retries are exhausted, the work queue entry is requeued for the next dispatcher cycle.
    /// After this many total failed attempts, the entry is left in Dispatched status and the
    /// dead letter mechanism handles it. Set to 0 to disable requeuing (fail immediately on
    /// first dispatch failure, preserving pre-1.2.0 behavior).
    /// </remarks>
    public int MaxDispatchAttempts { get; internal set; } = 5;

    /// <summary>
    /// Priority boost automatically applied to dependent train work queue entries.
    /// </summary>
    /// <remarks>
    /// When the ManifestManager creates work queue entries for dependent manifests
    /// (ScheduleType.Dependent), this value is added to the manifest's base priority,
    /// clamped to [0, 31]. Set to 0 to disable the automatic boost.
    /// </remarks>
    public int DependentPriorityBoost { get; internal set; } = 16;

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
    /// The default number of retries after a failed first run before a job is dead-lettered.
    /// </summary>
    /// <remarks>
    /// A manifest is dead-lettered once its counted failures exceed its retry count, so the
    /// default of 3 allows four attempts. This can be overridden per-manifest via
    /// <see cref="ScheduleOptions.MaxRetries"/>.
    /// </remarks>
    public int DefaultMaxRetries
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = 3;

    /// <summary>
    /// The default delay between retry attempts.
    /// </summary>
    /// <remarks>
    /// This can be combined with RetryBackoffMultiplier for exponential backoff. A retry is the
    /// run after a failed one: the delay applies only when the manifest's latest finished run
    /// failed, and grows with the failures inside <see cref="FailureCountWindow"/>. The run after
    /// a success or a cancel goes on time.
    /// </remarks>
    public TimeSpan DefaultRetryDelay
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Multiplier applied to retry delay on each subsequent retry (exponential backoff).
    /// </summary>
    /// <remarks>
    /// Set to 1.0 for constant retry delay. Default of 2.0 means delays of 5m, 10m, 20m, etc.
    /// </remarks>
    public double RetryBackoffMultiplier { get; internal set; } = 2.0;

    /// <summary>
    /// Maximum retry delay to prevent unbounded backoff growth.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; internal set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How far back a manifest's failed runs are counted toward its retry backoff and its
    /// dead letter.
    /// </summary>
    /// <remarks>
    /// A failed run counts while it started within this window before the current ManifestManager
    /// cycle, and after the manifest's latest resolved dead letter. A failure older than the window
    /// no longer delays a retry or counts toward <c>MaxRetries</c>, so occasional failures
    /// spread over weeks do not dead-letter a healthy manifest. A success does not reset the count
    /// inside the window, but the run after a success is not delayed. Defaults to 24 hours; must be positive. A manifest scheduled with its
    /// own <c>FailureWindow</c> uses that instead.
    /// </remarks>
    public TimeSpan FailureCountWindow { get; internal set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// A warning when <see cref="FailureCountWindow"/> is too short for
    /// <see cref="DefaultMaxRetries"/> to be reached, or null when it is long enough.
    /// </summary>
    /// <remarks>
    /// A manifest is dead-lettered once more than <c>MaxRetries</c> failures fall inside the
    /// window, but each retry waits out its backoff first (<see cref="DefaultRetryDelay"/> times
    /// <see cref="RetryBackoffMultiplier"/> per failure, capped at <see cref="MaxRetryDelay"/>).
    /// When those waits alone add up to the window or more, the oldest failure ages out before
    /// the last one happens, and a manifest that fails every time retries for ever without being
    /// dead-lettered. Logged at startup rather than refused: the window and the retry settings
    /// can each be changed at runtime, and the combination is a trade-off, not a value the
    /// scheduler cannot run with.
    /// </remarks>
    internal string? UnreachableRetriesWarning()
    {
        var retries = DefaultMaxRetries;
        if (retries <= 0)
            return null;

        var window = FailureCountWindow.TotalSeconds;
        var cap = MaxRetryDelay.TotalSeconds;
        var backoff = 0.0;

        for (var failure = 1; failure <= retries && backoff < window; failure++)
        {
            var delay = Math.Min(
                DefaultRetryDelay.TotalSeconds * Math.Pow(RetryBackoffMultiplier, failure - 1),
                cap
            );

            // Every later delay is the same from here, so the rest adds up at once.
            if (delay >= cap || delay <= 0)
            {
                backoff += delay * (retries - failure + 1);
                break;
            }

            backoff += delay;
        }

        if (backoff < window)
            return null;

        return $"FailureCountWindow ({FailureCountWindow}) is too short for DefaultMaxRetries "
            + $"({retries}): the retry backoff alone spaces a failing manifest's {retries + 1} "
            + $"runs over {TimeSpan.FromSeconds(Math.Min(backoff, TimeSpan.MaxValue.TotalSeconds - 1))}, "
            + "so its oldest failure leaves the window before the last one, and a manifest that "
            + "always fails retries for ever without being dead-lettered. Lengthen "
            + "FailureCountWindow, or lower DefaultMaxRetries, DefaultRetryDelay or MaxRetryDelay.";
    }

    /// <summary>
    /// Timeout after which a running job is cancelled when its manifest sets no Timeout.
    /// </summary>
    /// <remarks>
    /// Applies to a run a scheduler dispatched (it has a manifest, a work queue entry or a
    /// background job) whose manifest sets no TimeoutSeconds. A train nested inside a run takes
    /// the timeout of the run at the root of its chain. A run started on the train bus by a host
    /// that shares the database is not bounded by it; <see cref="StaleInProgressTimeout"/> still
    /// fails it if it never finishes.
    /// </remarks>
    public TimeSpan DefaultJobTimeout
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Timeout after which a Pending job that was never picked up is automatically failed.
    /// </summary>
    /// <remarks>
    /// Acts as a safety net for dispatch failures — if a job remains in Pending state
    /// longer than this duration, the ManifestManager's ReapStalePendingMetadataJunction
    /// will mark it as Failed. This catches cases where the job submitter failed to
    /// deliver the job (e.g. remote worker unreachable, Lambda timeout) and the
    /// immediate failure handling in DispatchJobsJunction also failed.
    /// </remarks>
    public TimeSpan StalePendingTimeout { get; internal set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Timeout after which an InProgress job that never completed is automatically failed.
    /// </summary>
    /// <remarks>
    /// Acts as a safety net for worker crashes, Lambda hard-kills, or OOM events where the
    /// process dies without reaching FinishServiceTrain. If a job remains in InProgress state
    /// longer than this duration, the ManifestManager's ReapStaleInProgressMetadataJunction
    /// will mark it as Failed. A run whose own timeout (its root manifest's Timeout, or
    /// <see cref="DefaultJobTimeout"/>) is longer is kept until that timeout plus the grace between
    /// the two defaults, so cooperative cancellation can land before the run is force-failed.
    /// </remarks>
    public TimeSpan StaleInProgressTimeout { get; internal set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// How long a work queue entry may stay unconfirmed before the ManifestManager resolves it.
    /// </summary>
    /// <remarks>
    /// Only a train with <c>DeferQueuePromotion</c> stages an entry unconfirmed, and normally it
    /// is confirmed a moment later, once its <c>OnQueue</c> hook returns. One still unconfirmed
    /// after this long belongs to a process that stopped in between. Keep it well above the
    /// slowest hook, because an entry resolved while its hook is still running is resolved
    /// wrongly.
    /// </remarks>
    public TimeSpan StaleStagedEntryTimeout { get; internal set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether a stale unconfirmed entry is promoted instead of cancelled. Defaults to false.
    /// </summary>
    /// <remarks>
    /// Cancelling is the safe default: nothing recorded tells a hook that succeeded from one that
    /// never ran or one that rejected the mutation. Promote only when every deferring train's
    /// chain re-checks what its hook checked and its hook is idempotent.
    /// </remarks>
    public bool PromoteStaleStagedEntries { get; internal set; }

    /// <summary>
    /// The default misfire policy applied to manifests that do not specify one.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="MisfirePolicy.FireOnceNow"/> for backward compatibility.
    /// Can be overridden per-manifest via the ScheduleOptions fluent API.
    /// </remarks>
    public MisfirePolicy DefaultMisfirePolicy { get; internal set; } = MisfirePolicy.FireOnceNow;

    /// <summary>
    /// The default misfire threshold — the grace period for determining when a missed
    /// run should trigger the misfire policy.
    /// </summary>
    /// <remarks>
    /// If a manifest is overdue by less than this threshold, it fires normally regardless
    /// of its <see cref="MisfirePolicy"/>. If overdue by more, the manifest's MisfirePolicy
    /// determines behavior. Can be overridden per-manifest via MisfireThresholdSeconds.
    /// </remarks>
    public TimeSpan DefaultMisfireThreshold { get; internal set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether to automatically recover stuck jobs on scheduler startup.
    /// </summary>
    /// <remarks>
    /// If true, every <c>InProgress</c> run in the shared database whose <c>StartTime</c> is
    /// earlier than this host's start is marked <c>Failed</c> ("Server restarted while job was in
    /// progress").
    /// That is every such run, whichever host or worker is executing it: the recovery does not
    /// know which runs belonged to this process, so on a deployment where several hosts or
    /// remote workers share the database, starting one host also fails runs that are still
    /// executing elsewhere. The recovery itself requeues nothing. Skipped when no database
    /// provider is registered.
    /// </remarks>
    public bool RecoverStuckJobsOnStartup
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = true;

    /// <summary>
    /// How long to retain dead letter records after resolution.
    /// </summary>
    public TimeSpan DeadLetterRetentionPeriod { get; internal set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether resolved dead letters older than <see cref="DeadLetterRetentionPeriod"/> are deleted
    /// automatically. Read on each cleanup run, so turning it off at runtime keeps them from the
    /// next run on.
    /// </summary>
    public bool AutoPurgeDeadLetters { get; internal set; } = true;

    /// <summary>
    /// The interval at which the dead letter cleanup service checks for expired dead letters.
    /// </summary>
    public TimeSpan DeadLetterCleanupInterval { get; internal set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Configuration for automatic metadata cleanup, if enabled.
    /// </summary>
    /// <remarks>
    /// When set (via <c>.AddMetadataCleanup()</c>), a background service will
    /// periodically delete old metadata entries for the configured train types.
    /// Null means metadata cleanup is disabled.
    /// </remarks>
    public MetadataCleanupConfiguration? MetadataCleanup { get; internal set; }

    /// <summary>
    /// The maximum number of queued work queue entries loaded per manifest group per JobDispatcher cycle.
    /// </summary>
    /// <remarks>
    /// Controls the per-group batch limit in <see cref="Trains.JobDispatcher.Junctions.LoadQueuedJobsJunction"/>.
    /// Uses a window function (<c>ROW_NUMBER() OVER (PARTITION BY manifest_group_id)</c>) to load
    /// up to this many entries from each group, ensuring every group with queued work is represented
    /// in the loaded batch. This prevents a single high-priority group from monopolizing the batch
    /// and starving lower-priority groups.
    ///
    /// Manual entries (no manifest) are always included regardless of this limit.
    /// Set to null to disable the limit (load all queued entries without group-fair batching).
    /// </remarks>
    public int? MaxQueuedJobsPerCycle { get; internal set; } = 100;

    /// <summary>
    /// The maximum number of work queue entries created per ManifestManager polling cycle.
    /// </summary>
    /// <remarks>
    /// Controls how many entries <see cref="Trains.ManifestManager.Junctions.CreateWorkQueueEntriesJunction"/>
    /// creates per cycle. When more manifests are due than this limit, entries are distributed
    /// fairly across manifest groups: each group gets a base allocation of
    /// <c>limit / numGroups</c>, with overflow slots going to higher-priority groups first.
    /// This prevents a single large group from monopolizing the batch and starving smaller
    /// groups (e.g., 5000 cache manifests crowding out 15 delta manifests).
    /// Set to null to disable the limit (unlimited — all due manifests are enqueued per cycle).
    /// </remarks>
    public int? MaxWorkQueueEntriesPerCycle
    {
        get;
        [EditorBrowsable(EditorBrowsableState.Never)]
        set;
    } = 200;

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
    ///
    /// Only this application's manifests are considered. Every manifest the scheduler writes
    /// records the application that declared it (<c>Manifest.Owner</c>, the host's
    /// <c>IHostEnvironment.ApplicationName</c>, or the entry assembly's name when there is no host
    /// environment), and the prune compares only the manifests carrying this application's name.
    /// A manifest another application owns, and one with no owner (written before the column
    /// existed and not declared since), is never deleted by it. A host that declares no manifests,
    /// or whose application name cannot be found, prunes nothing, and a manifest with a pending or
    /// running run is kept until the run finishes.
    /// </remarks>
    public bool PruneOrphanedManifests { get; internal set; } = true;

    /// <summary>
    /// The application name every manifest this scheduler writes records as its owner, and the
    /// only owner the startup prune deletes from. Set by the startup service from the host
    /// environment; null until then, and when no name can be found.
    /// </summary>
    internal string? Owner { get; set; }

    /// <summary>
    /// Whether a real database provider (e.g. PostgreSQL) is configured.
    /// </summary>
    /// <remarks>
    /// Set during <c>Build()</c> based on <c>HasDatabaseProvider</c>. When false (e.g. InMemory),
    /// startup operations that require database-specific features like <c>ExecuteDeleteAsync</c>
    /// are skipped — the in-memory database starts empty on each restart, so prune and
    /// recover operations are unnecessary.
    /// </remarks>
    internal bool HasDatabaseProvider { get; set; }

    /// <summary>
    /// Whether this configuration is the one <c>AddScheduler</c> built, rather than the empty one
    /// <c>AddTraxJobRunner</c> registers on a runner or API-only host. Only a scheduler host knows
    /// the values the scheduler runs with, so only there can a settings save tell that a value is
    /// already in effect and leave it out of the persisted row.
    /// </summary>
    internal bool IsSchedulerHost { get; set; }

    /// <summary>
    /// The value <c>AutoPurgeDeadLetters(bool)</c> stated in code, or null when the builder left
    /// the default. A saved setting may turn the purge off but never on over a code
    /// <c>false</c>: the purge deletes, so it runs only when both allow it.
    /// </summary>
    internal bool? ConfiguredAutoPurgeDeadLetters { get; set; }

    /// <summary>
    /// The value <c>DeadLetterRetentionPeriod(TimeSpan)</c> stated in code, or null when the
    /// builder left the default. A saved retention may lengthen it but never shorten it: the longer
    /// of the two applies.
    /// </summary>
    internal TimeSpan? ConfiguredDeadLetterRetentionPeriod { get; set; }

    /// <summary>
    /// How often a running scheduler checks the persisted settings row for a save made by another
    /// host (or this one) and applies it. Zero turns the check off; the row is then read only at
    /// startup.
    /// </summary>
    internal TimeSpan SettingsRefreshInterval { get; set; } = TimeSpan.FromSeconds(5);
}

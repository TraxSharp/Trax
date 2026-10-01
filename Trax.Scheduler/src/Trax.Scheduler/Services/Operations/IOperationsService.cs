namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Shared service for high-level operations performed by both the dashboard UI and the
/// GraphQL <c>operations</c> namespace. Centralising the logic here keeps both surfaces
/// behaviourally identical: a queue/cancel/update from the React (or Blazor) dashboard
/// runs the same code path as the same call from the GraphQL API.
/// </summary>
public interface IOperationsService
{
    /// <summary>
    /// Queues a train through the mediator's <c>ITrainExecutionService.QueueAsync</c>, so the
    /// train's authorization, its <c>OnQueue</c> hook and its subject key apply.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Id: newEntryId, Count: 1, ...)</c> on success;
    /// <c>OperationResult(false, ...)</c> with a populated <c>Message</c> for a missing
    /// <c>TrainName</c>, an unknown train, or invalid or oversized <c>InputJson</c>.
    /// <para>
    /// A refusal of the enqueue is also returned as a failed result: the <c>OnQueue</c> hook or
    /// <c>QueueSubjectKey</c> threw, the subject key was unusable, or a deferred entry was
    /// cancelled before it was confirmed. Its message is
    /// <c>"The enqueue was refused: {exception message}"</c> only when the exception is a plain
    /// <c>TrainException</c> (not a type derived from it), a <c>QueuedWorkCancelledException</c>
    /// or a <c>QueueHookTimeoutException</c>; for any other type it is the fixed
    /// <c>"The enqueue was refused."</c>, and the exception is logged at Warning. A hook that
    /// refuses with a message for the caller throws <c>TrainException</c> (scheduler/0004).
    /// </para>
    /// </returns>
    /// <exception cref="System.Data.Common.DbException">
    /// The enqueue failed on infrastructure rather than being refused. Not only this type: a
    /// database, EF Core, network, I/O or timeout exception anywhere in the exception's chain
    /// counts (scheduler/0004). It is logged and rethrown as it was thrown, so its message never
    /// becomes a result's <c>Message</c>.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The caller may not run the train (a <c>TrainAuthorizationException</c> when the API's
    /// authorization is registered). It propagates rather than becoming a failed result.
    /// </exception>
    /// <exception cref="Trax.Mediator.Exceptions.TrainAuthorizationNotConfiguredException">
    /// The train declares <c>[TraxAuthorize]</c> and the host registered no
    /// <c>ITrainAuthorizationService</c>. A host misconfiguration, so it is logged and thrown
    /// rather than reported as a refusal (scheduler/0004).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled. It propagates rather than becoming a failed result.
    /// </exception>
    Task<OperationResult> QueueTrainAsync(QueueTrainInput input, CancellationToken ct);

    /// <summary>
    /// Runs a train now: creates its <c>Pending</c> metadata row and hands it, with its input,
    /// to the job submitter the train is routed to (the same routing the job dispatcher uses).
    /// Nothing is written to the work queue, so the run skips dispatch ordering, group limits
    /// and the subject lock of <c>docs/0019</c>: it is a deliberate bypass, for an operator who
    /// wants the train to start at once.
    /// </summary>
    /// <remarks>
    /// The mediator prepares the run (<c>ITrainExecutionService.PrepareAsync</c>), so the train's
    /// <c>[TraxAuthorize]</c> requirements are checked as they are for
    /// <see cref="QueueTrainAsync"/>, before the input is read, and the input is read as every
    /// caller's input is (<c>TrainInputReader</c>, <c>docs/0023</c>): property names matched
    /// whatever their case, a property given twice refused, JSON reference metadata not honoured,
    /// the input size cap, and a blank input standing for an empty object. The stored form the
    /// submitter writes for the worker is then held to the cap a queued input's stored form is
    /// held to, <c>TrainInputReader.StoredInputGrowthFactor</c> times <c>MaxInputJsonBytes</c>.
    /// <para>
    /// A run then applies the per-record checks a queue applies (<c>docs/0037</c>). The train's
    /// <c>OnQueue</c> hook, when it overrides one, runs on the run's input before the metadata row
    /// is saved, as it runs for an enqueue: <c>TrainInput</c> reads the input, the hook's
    /// <c>metadata.ExternalId</c> is the one the run executes under, writes on the enqueue context
    /// are saved with the run's row, and <c>MaxQueueHookDuration</c> bounds it. It runs inside a
    /// trusted scope too. A train that overrides <c>QueueSubjectKey</c> is refused outside a
    /// trusted scope, because a run bypasses the subject lock the queue holds for it; the message
    /// says to queue it instead.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <c>OperationResult(true, Id: metadataId, Count: 1, ...)</c> once the job is submitted; the
    /// id is the run's metadata id, not a work queue id. Also a success when the submitter threw
    /// after a runner had already started the run (a remote runner that answered with the train's
    /// error, a call that timed out while the run went on, or an in-process submitter that ran a
    /// failing train): the run owns its outcome, which its row records, and the message says the
    /// outcome is pending on the run. <c>OperationResult(false, ...)</c> with a populated
    /// <c>Message</c> for a missing <c>TrainName</c>, an unknown train, invalid or oversized
    /// <c>InputJson</c>, an input whose stored form is over its cap, a subject-keyed train outside
    /// a trusted scope, or a refusal by the <c>OnQueue</c> hook; no metadata row is written for any
    /// of these. A refusal's message follows <see cref="QueueTrainAsync"/>'s rule with "run" for
    /// "enqueue": <c>"The run was refused: {exception message}"</c> for a plain
    /// <c>TrainException</c> or a <c>QueueHookTimeoutException</c>, the fixed
    /// <c>"The run was refused."</c> for any other type.
    /// </returns>
    /// <exception cref="UnauthorizedAccessException">
    /// The caller may not run the train. It propagates rather than becoming a failed result, and
    /// no metadata row is written.
    /// </exception>
    /// <exception cref="Trax.Mediator.Exceptions.TrainAuthorizationNotConfiguredException">
    /// The train declares <c>[TraxAuthorize]</c>, no <c>ITrainAuthorizationService</c> is
    /// registered, the call is not in a trusted scope and the host did not opt out with
    /// <c>AllowMissingAuthorizationService()</c>. A host misconfiguration, so it is thrown rather
    /// than reported as a refusal.
    /// </exception>
    /// <exception cref="Exception">
    /// The job submitter failed and no runner started the run. The run's metadata row is marked
    /// <c>Failed</c> with that exception, as the job dispatcher does for a failed dispatch, and the
    /// exception is logged and rethrown: the train was accepted and the server could not start
    /// it, which is not a refusal (scheduler/0004). A database failure writing the row, or one
    /// anywhere in the chain of what the <c>OnQueue</c> hook threw, propagates the same way.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled before the run's metadata row was written. Once the row
    /// is written the submit does not take <paramref name="ct"/>, so a caller that goes away does
    /// not cancel the run it asked for.
    /// </exception>
    Task<OperationResult> RunTrainAsync(RunTrainInput input, CancellationToken ct) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not implement RunTrainAsync. It was added to "
                + "IOperationsService after this implementation was written."
        );

    /// <summary>
    /// Transitions a queued work queue entry to <c>Cancelled</c>. Only entries currently
    /// in the <c>Queued</c> state are eligible. Entries that are already dispatched or
    /// already cancelled return a failure result without modifying the row.
    /// </summary>
    Task<OperationResult> CancelWorkQueueEntryAsync(long id, CancellationToken ct);

    /// <summary>
    /// Requests cancellation of the given runs: every one still <c>Pending</c> or
    /// <c>InProgress</c> has <c>CancellationRequested</c> set, which a run observes at its next
    /// junction boundary on any host (a <c>Pending</c> run is recorded <c>Cancelled</c> without
    /// running when the job runner picks it up), and each is also cancelled at once through the
    /// <c>ICancellationRegistry</c> when it runs on this host. Terminal and unknown ids are
    /// skipped. <c>ITraxScheduler.CancelAsync</c> and <c>CancelGroupAsync</c> apply the same
    /// rule to a manifest's or a group's runs.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number of runs flagged,
    /// zero included. <c>OperationResult(false, ...)</c> for an empty list or more than
    /// <c>OperationsService.MaxBatchSize</c> ids, with nothing flagged.
    /// </returns>
    Task<OperationResult> CancelExecutionsAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(CancelExecutionsAsync));

    /// <summary>
    /// Cancels the given work queue entries that are still <c>Queued</c>, in one statement, so an
    /// entry dispatched meanwhile is left alone. Other ids are skipped. Signals
    /// <c>ChangeDomain.WorkQueue</c> when any entry changed.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number cancelled, zero
    /// included; <c>OperationResult(false, ...)</c> for an empty list or too many ids.
    /// </returns>
    Task<OperationResult> CancelWorkQueueEntriesAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(CancelWorkQueueEntriesAsync));

    /// <summary>
    /// Enables or disables the given manifests by id. Only manifests whose flag differs are
    /// written, and <c>ChangeDomain.Manifest</c> is signalled when any did.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number changed, zero
    /// included; <c>OperationResult(false, ...)</c> for an empty list or too many ids.
    /// </returns>
    Task<OperationResult> SetManifestsEnabledAsync(
        IReadOnlyCollection<long> ids,
        bool enabled,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(SetManifestsEnabledAsync));

    /// <summary>
    /// Enables or disables the given manifest groups by id. Only groups whose flag differs are
    /// written, with <c>UpdatedAt</c> bumped, and <c>ChangeDomain.ManifestGroup</c> is signalled
    /// when any did.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number changed, zero
    /// included; <c>OperationResult(false, ...)</c> for an empty list or too many ids.
    /// </returns>
    Task<OperationResult> SetManifestGroupsEnabledAsync(
        IReadOnlyCollection<long> ids,
        bool enabled,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(SetManifestGroupsEnabledAsync));

    /// <summary>
    /// Enables or disables every manifest group, as <see cref="SetManifestGroupsEnabledAsync"/>
    /// does for a list. A separate method so that "all" is never what an empty or missing list
    /// means.
    /// </summary>
    Task<OperationResult> SetAllManifestGroupsEnabledAsync(bool enabled, CancellationToken ct) =>
        throw NotImplementedBy(nameof(SetAllManifestGroupsEnabledAsync));

    /// <summary>
    /// Run counts by state for one manifest, with its most recent run and most recent successful
    /// run. A manifest with no runs, or an id with no manifest, gets zeros and nulls.
    /// </summary>
    Task<ManifestExecutionStats> GetManifestExecutionStatsAsync(
        long manifestId,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(GetManifestExecutionStatsAsync));

    /// <summary>
    /// Manifest and run counts for each of the given groups, one row per distinct id in the
    /// order given, zeros for a group with no manifests or runs. Batched so a list page fetches
    /// the stats of its visible groups in one call.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// More than <c>OperationsService.MaxBatchSize</c> distinct ids.
    /// </exception>
    Task<IReadOnlyList<ManifestGroupExecutionStats>> GetManifestGroupExecutionStatsAsync(
        IReadOnlyCollection<long> groupIds,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(GetManifestGroupExecutionStatsAsync));

    /// <summary>
    /// A page of log entries, newest first, filtered by run, minimum level and exact category.
    /// Pages by keyset when <see cref="LogQuery.AfterId"/> is set, otherwise by offset. The page
    /// size is clamped to 1 through <c>OperationsService.MaxPageSize</c>.
    /// </summary>
    Task<LogPage> GetLogsAsync(LogQuery query, CancellationToken ct) =>
        throw NotImplementedBy(nameof(GetLogsAsync));

    /// <summary>
    /// The exact number of log entries matching the query's filter; its paging fields and cursor
    /// are ignored. An exact count of an unfiltered log table is a full scan, so a caller that
    /// only needs a size for a pager on a large table may prefer an estimate.
    /// </summary>
    Task<int> CountLogsAsync(LogQuery query, CancellationToken ct) =>
        throw NotImplementedBy(nameof(CountLogsAsync));

    private NotSupportedException NotImplementedBy(string member) =>
        new(
            $"{GetType().Name} does not implement {member}. It was added to IOperationsService "
                + "after this implementation was written."
        );

    /// <summary>
    /// Patches mutable settings on a manifest group (max active jobs, priority, enabled
    /// flag). Each field on <paramref name="input"/> is optional and "no change by default":
    /// only properties explicitly set on the input are written. <c>UpdatedAt</c> is bumped
    /// when at least one field changed.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Id: groupId, Count: N, ...)</c> where <c>N</c> is the number
    /// of fields written; <c>OperationResult(false, ...)</c> if the group does not exist, or if
    /// <c>Priority</c> is outside 0 to 31 or <c>MaxActiveJobs</c> is below 1, in which case no
    /// field of the patch is written.
    /// </returns>
    Task<OperationResult> UpdateManifestGroupAsync(
        long id,
        UpdateManifestGroupInput input,
        CancellationToken ct
    );

    /// <summary>
    /// Returns the 1-hop cross-group dependency neighborhood for a manifest group:
    /// every group that contains a manifest the focal group's manifests depend on
    /// (upstream), every group that contains a manifest depending on the focal group's
    /// manifests (downstream), and the focal group itself. Edges are directed
    /// parent → dependent.
    /// </summary>
    /// <returns>
    /// <c>null</c> if the group does not exist or contains no manifests with cross-group
    /// dependencies; otherwise a graph that always includes the focal group as a node.
    /// </returns>
    Task<ManifestGroupDependencyGraph?> GetManifestGroupDependencyGraphAsync(
        long groupId,
        CancellationToken ct
    );

    /// <summary>
    /// Returns the whole cross-group dependency graph: every manifest group as a node and every
    /// cross-group dependency (a manifest in one group depending on a manifest in another) as a
    /// directed parent → dependent edge. Nothing is highlighted. Backs the global dependency
    /// graph on the dashboard's manifest-groups page.
    /// </summary>
    Task<ManifestGroupDependencyGraph> GetGlobalManifestGroupGraphAsync(CancellationToken ct);

    /// <summary>
    /// Returns a snapshot of dashboard-relevant metrics: today's KPI counts, an
    /// executions-over-time chart at the chosen granularity, top failing trains over
    /// the last 7 days, top average durations over the last 7 days, and per-train
    /// throughput sparklines over the last 7 days (28 6-hour buckets).
    /// </summary>
    /// <param name="range">Granularity of the executions-over-time chart only.</param>
    /// <param name="hideAdminTrains">
    /// When true, framework admin trains (matching <c>AdminTrains.FullNames</c>) are
    /// excluded from every series. Mirrors the dashboard's "Hide admin trains" toggle.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<DashboardMetrics> GetDashboardMetricsAsync(
        MetricsRange range,
        bool hideAdminTrains,
        CancellationToken ct
    );

    /// <summary>
    /// Returns a snapshot of host-process health: working set, GC heap, uptime, and
    /// process start time. Synchronous because all data comes from
    /// <see cref="System.Diagnostics.Process"/>.
    /// </summary>
    ServerMetrics GetServerMetrics();

    /// <summary>
    /// Returns the live scheduler runtime settings, reading from the in-memory
    /// <c>SchedulerConfiguration</c> singleton (and <c>LocalWorkerOptions</c> /
    /// <c>MetadataCleanupConfiguration</c> if registered). The singleton is the
    /// source of truth at runtime; on a scheduler host the persisted row is loaded into it at
    /// startup and again within seconds of any save, by <c>SchedulerConfigBootstrapHostedService</c>.
    /// </summary>
    SchedulerConfigSnapshot GetSchedulerConfig();

    /// <summary>
    /// Patches the scheduler runtime settings. Only the fields the patch sets are written to the
    /// persisted <c>trax.scheduler_config</c> row, so a save never rewrites a setting it did not
    /// name. The host that saves applies the change at once, and every running scheduler host
    /// picks the row up within seconds (<c>SchedulerConfiguration</c>'s settings refresh), so a
    /// save made on an API-only host reaches the scheduler without a restart. A scheduler applies a
    /// change from its next polling cycle; <see cref="UpdateSchedulerConfigInput.LocalWorkerCount"/>
    /// is the exception and applies when the worker pool next starts.
    /// </summary>
    /// <remarks>
    /// The row records which settings a save named. Those replace the values configured in code
    /// on every scheduler host; every other setting keeps each host's code value, so a later change
    /// in code applies to it. Any host may make the first save. A field counts as changed when it
    /// differs from the stored value, or, for a setting no save has named, from the value a
    /// scheduler host runs with; a host that does not run the scheduler cannot know that value, so
    /// there every field the patch sets is stored.
    /// </remarks>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number of
    /// fields actually changed. <c>OperationResult(false, ...)</c>, naming each offending field,
    /// when a value is outside the range the scheduler can run with: a polling or cleanup interval
    /// outside 1 second to 30 days; a job timeout, stale-pending timeout or metadata retention
    /// under 1 second; a negative retry count, retry delay or dead-letter retention; any duration
    /// over ten years; a <c>MaxActiveJobs</c> below 1; a <c>LocalWorkerCount</c> outside 1 to
    /// 256; a failure count window outside 1 second to ten years; or a backoff multiplier below 1
    /// or not finite. A refused patch applies nothing.
    /// </returns>
    Task<OperationResult> UpdateSchedulerConfigAsync(
        UpdateSchedulerConfigInput input,
        CancellationToken ct
    );
}

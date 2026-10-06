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
    /// <para>
    /// On a host with no database provider nothing dispatches the work queue, so the enqueue is
    /// refused before anything is written, with <c>OperationsService.NoDispatcherMessage</c>
    /// (scheduler ADR 0019). <see cref="RequeueExecutionAsync(long, bool, CancellationToken)"/>
    /// and the triggers refuse the same way.
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
    /// Re-queues a run: queues a fresh run of the same train with the input the run recorded. The
    /// GraphQL <c>requeueExecution</c> mutation and the dashboard's Re-queue button both call it,
    /// so the two refuse the same runs with the same messages and enqueue the same way.
    /// </summary>
    /// <remarks>
    /// The run is read by <paramref name="metadataId"/>, and is refused without queueing when its
    /// saved input is not the input it ran with: nothing was saved (inputs are saved only when
    /// <c>SaveTrainParameters()</c> is on), the parameter effect saved a <c>_truncated</c>,
    /// <c>_unserializable</c> or <c>_disposed</c> placeholder in its place, or
    /// <c>[TraxSensitive]</c> members were masked. Each of those would read back as defaults.
    /// <para>
    /// The saved input carries the reference metadata <c>SaveTrainParameters()</c> writes
    /// (<c>$id</c>, <c>$values</c>, <c>$ref</c>). It is rewritten as the plain tree it stands for
    /// by <c>TrainInputReader.ResolveSavedInput</c> before it is queued, so a list, and an object
    /// the input held twice, come back as they were; a saved input whose metadata has no plain
    /// form, or whose plain form is over the input cap, is refused.
    /// </para>
    /// <para>
    /// The enqueue then goes through the same path as <see cref="QueueTrainAsync"/>, so the train's
    /// authorization, its <c>OnQueue</c> hook, its subject key and the input cap apply, and a
    /// refusal or failure is reported as it is there. The caller's trusted scope carries through:
    /// the dashboard calls this inside its <c>"dashboard"</c> scope, the API does not.
    /// </para>
    /// <para>
    /// When the run recorded decisions, or was itself queued to replay another run's, the new
    /// entry names it as the run whose decisions it replays, so the new run takes the tracks the
    /// original took (central <c>docs/0041</c>); a question the run never reached is answered from
    /// the run it replayed in turn. A run with neither is re-queued exactly as an ordinary enqueue. The link is only ever set
    /// here, to the run being re-queued, so it always points at a run of the same train. A run
    /// whose answers a queued entry or another run already replays is re-queued to ask afresh
    /// (scheduler/0017), and the message says so.
    /// </para>
    /// </remarks>
    /// <param name="metadataId">The id of the run (metadata row) to re-queue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>OperationResult(true, Id: newEntryId, Count: 1, ...)</c> on success. A failed result,
    /// with a message, when no run has the id (<c>"Execution {id} not found."</c>), its train is
    /// no longer registered (<c>"Train {name} is no longer registered, ..."</c>), its saved input
    /// cannot be re-queued or no longer reads as the train's input type (<c>"The saved input of
    /// run {id} no longer reads as {type}: ..."</c>), or the enqueue is refused as
    /// <see cref="QueueTrainAsync"/> describes.
    /// </returns>
    /// <exception cref="Trax.Mediator.Exceptions.DecisionReplayNotSupportedException">
    /// The run recorded decisions and the registered <c>ITrainExecutionService</c> does not
    /// implement the overload that carries the replay link. A host misconfiguration, so it is
    /// logged and thrown rather than reported as a refusal (scheduler/0004).
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The caller may not queue the train. It propagates, as from <see cref="QueueTrainAsync"/>.
    /// </exception>
    /// <exception cref="Trax.Mediator.Exceptions.TrainAuthorizationNotConfiguredException">
    /// As from <see cref="QueueTrainAsync"/>.
    /// </exception>
    Task<OperationResult> RequeueExecutionAsync(long metadataId, CancellationToken ct) =>
        throw NotImplementedBy(nameof(RequeueExecutionAsync));

    /// <summary>
    /// Re-queues a run, as <see cref="RequeueExecutionAsync(long, CancellationToken)"/> does,
    /// optionally asking its deciders afresh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <paramref name="askAfresh"/> false this is
    /// <see cref="RequeueExecutionAsync(long, CancellationToken)"/>. With it true the new entry
    /// carries no replay link, so the new run asks every question again; nothing else differs.
    /// </para>
    /// <para>
    /// A run's answers are replayed once (scheduler/0017). When a queued entry (a manifest's
    /// retry, or an earlier requeue) or a run, in any state, already replays the run being
    /// re-queued, the requeue still queues the run but asks afresh, and says so in the result's
    /// message.
    /// </para>
    /// </remarks>
    /// <param name="metadataId">The id of the run (metadata row) to re-queue.</param>
    /// <param name="askAfresh">True queues the run to ask its deciders afresh.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>As <see cref="RequeueExecutionAsync(long, CancellationToken)"/>.</returns>
    /// <exception cref="NotSupportedException">The implementation predates this overload.</exception>
    Task<OperationResult> RequeueExecutionAsync(
        long metadataId,
        bool askAfresh,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(RequeueExecutionAsync));

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
    /// Sets whether retries of the given manifests replay the decisions of the run they retry
    /// (<c>ReplayDecisionsOnRetry</c>, scheduler/0017). Only manifests whose flag differs are
    /// written, and <c>ChangeDomain.Manifest</c> is signalled when any did. Turning it off also
    /// clears the replay link of each manifest's queued entry, so a retry waiting out its backoff
    /// asks afresh, and signals <c>ChangeDomain.WorkQueue</c> when it cleared any. The flag and the
    /// links are written in one transaction.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number of manifests
    /// changed, zero included; the message also counts the queued retries whose link was cleared,
    /// which a manifest already set to ask afresh can still have. <c>OperationResult(false, ...)</c>
    /// for an empty list or too many ids.
    /// </returns>
    Task<OperationResult> SetManifestsReplayDecisionsOnRetryAsync(
        IReadOnlyCollection<long> ids,
        bool replay,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(SetManifestsReplayDecisionsOnRetryAsync));

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
    /// Triggers the manifest with this external id, as <c>ITraxScheduler.TriggerAsync</c> does,
    /// and says what the trigger did. The dashboard's Trigger buttons and the API's
    /// <c>triggerManifest</c> and <c>triggerManifestDelayed</c> all call it (central
    /// <c>docs/0022</c>).
    /// </summary>
    /// <remarks>
    /// The trigger queues a new entry marked as asked for by name, so it runs even while the
    /// manifest is disabled. A manifest holds at most one queued entry, so when it already has one
    /// nothing more is queued and that entry becomes the triggered run: it is marked the same way
    /// and, when it was due later than the trigger asked, brought forward. The result says which
    /// happened. With <paramref name="askAfresh"/> true, a released retry asks its deciders afresh
    /// rather than replaying the failed run's decisions (scheduler/0017); when the dispatcher had
    /// claimed it first it is too late to change, the trigger still succeeds, and
    /// <see cref="TriggerManifestResult.StillReplaying"/> is true.
    /// </remarks>
    /// <param name="externalId">The manifest's external id.</param>
    /// <param name="delay">
    /// How long from now the run becomes due; <c>null</c> (or zero or less) means now. An existing
    /// entry due sooner keeps its own time.
    /// </param>
    /// <param name="askAfresh">True asks a released retry's deciders afresh.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// What the trigger did. A failed result, with nothing changed, when no manifest has the
    /// external id or when nothing on this host can dispatch a queued run (scheduler ADR 0019).
    /// </returns>
    /// <exception cref="System.Data.Common.DbException">
    /// A save failed on infrastructure. It is thrown, not reported (scheduler/0004).
    /// </exception>
    Task<TriggerManifestResult> TriggerManifestAsync(
        string externalId,
        TimeSpan? delay,
        bool askAfresh,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(TriggerManifestAsync));

    /// <summary>
    /// Triggers each of the given manifests now, exactly as <c>ITraxScheduler.TriggerAsync</c>
    /// triggers one by external id: a new work queue entry marked as asked for by name, so it runs
    /// even while the manifest is disabled, or, when the manifest already has a queued entry, that
    /// entry released as the triggered run and brought forward to now. The manifests are
    /// triggered one at a time in the order given, each with its own save, and
    /// <c>ChangeDomain.WorkQueue</c> is signalled once.
    /// </summary>
    /// <remarks>
    /// With <paramref name="askAfresh"/> true, a released entry that would replay a failed run's
    /// decisions (a retry waiting out its backoff) asks its deciders afresh instead
    /// (scheduler/0017). When the dispatcher claimed that entry first it is too late to change: the
    /// manifest is counted in <see cref="BatchTriggerResult.TooLateToAskAfresh"/> with a note naming the
    /// run it replays. Ids that name no manifest are skipped and noted; they do not stop the rest.
    /// </remarks>
    /// <param name="manifestIds">The manifests' database ids (not their external ids).</param>
    /// <param name="askAfresh">True asks a released retry's deciders afresh.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// What the batch did. A failed result, with nothing triggered, for an empty list, more than
    /// <c>OperationsService.MaxBatchSize</c> ids, or a host where nothing can dispatch a queued
    /// run (scheduler ADR 0019).
    /// </returns>
    /// <exception cref="System.Data.Common.DbException">
    /// A save failed on infrastructure. It is thrown, not reported (scheduler/0004); the manifests
    /// triggered before it stay triggered, and sending the same batch again is safe, since a
    /// manifest holds at most one queued entry.
    /// </exception>
    Task<BatchTriggerResult> TriggerManifestsAsync(
        IReadOnlyCollection<long> manifestIds,
        bool askAfresh,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(TriggerManifestsAsync));

    /// <summary>
    /// Triggers every eligible manifest of each of the given groups, as
    /// <c>ITraxScheduler.TriggerGroupAsync</c> does for one group: the enabled members that run on
    /// their own schedule (not Dependent or DormantDependent), each triggered as
    /// <see cref="TriggerManifestsAsync"/> triggers one. A disabled group is triggered too, as the
    /// single group trigger triggers it; the group still holds its runs until it is enabled.
    /// <c>ChangeDomain.WorkQueue</c> is signalled once.
    /// </summary>
    /// <param name="groupIds">The groups' ids.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// What the batch did, counting manifests; <see cref="BatchTriggerResult.Matched"/> counts the
    /// groups found and <see cref="BatchTriggerResult.Skipped"/> the ids that named none. A failed
    /// result, with nothing triggered, for an empty list, more than
    /// <c>OperationsService.MaxBatchSize</c> ids, or a host where nothing can dispatch a queued
    /// run (scheduler ADR 0019).
    /// </returns>
    /// <exception cref="System.Data.Common.DbException">
    /// As from <see cref="TriggerManifestsAsync"/>.
    /// </exception>
    Task<BatchTriggerResult> TriggerManifestGroupsAsync(
        IReadOnlyCollection<long> groupIds,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(TriggerManifestGroupsAsync));

    /// <summary>
    /// Requests cancellation of every <c>Pending</c> or <c>InProgress</c> run of every manifest in
    /// the given groups, in one statement, by the rule <see cref="CancelExecutionsAsync"/> applies
    /// to a list of runs and <c>ITraxScheduler.CancelGroupAsync</c> to one group.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: N, ...)</c> where <c>N</c> is the number of runs flagged,
    /// zero included; the message also says how many of the ids named a group.
    /// <c>OperationResult(false, ...)</c> for an empty list or too many ids, with nothing flagged.
    /// </returns>
    Task<OperationResult> CancelManifestGroupsAsync(
        IReadOnlyCollection<long> groupIds,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(CancelManifestGroupsAsync));

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
    /// A page of log entries, filtered by run, minimum level, exact category, and text the message
    /// or the category contains, newest first unless <see cref="LogQuery.Order"/> says oldest
    /// first. Pages by keyset when <see cref="LogQuery.AfterId"/> is set, in the chosen order,
    /// otherwise by offset. The page size is clamped to 1 through
    /// <c>OperationsService.MaxPageSize</c>.
    /// </summary>
    Task<LogPage> GetLogsAsync(LogQuery query, CancellationToken ct) =>
        throw NotImplementedBy(nameof(GetLogsAsync));

    /// <summary>
    /// The exact number of log entries matching the query's filter, its text filters included;
    /// its order, paging fields and cursor are ignored. An exact count of an unfiltered log table is a full scan, so a caller that
    /// only needs a size for a pager on a large table may prefer an estimate.
    /// </summary>
    Task<int> CountLogsAsync(LogQuery query, CancellationToken ct) =>
        throw NotImplementedBy(nameof(CountLogsAsync));

    /// <summary>
    /// The number of log entries matching the query's filter, for a pager: exact, as
    /// <see cref="CountLogsAsync"/> gives it, when the query has no text filter, and counted only
    /// up to <c>OperationsService.LogCountCap</c> (10,000) when it has one
    /// (<see cref="LogQuery.MessageContains"/> or <see cref="LogQuery.CategoryContains"/>). Its
    /// order, paging fields and cursor are ignored.
    /// </summary>
    /// <remarks>
    /// <para>A text filter that matches most entries matches a large share of a large table, and
    /// an exact count of those reads every one of them (about 325 ms at three million rows on
    /// Postgres). Capped, the count stops at the first 10,001 matches. When more than 10,000
    /// entries match, the result is <c>Count = 10000</c> with <see cref="LogCount.Capped"/> set,
    /// to be shown as "10,000+"; exactly 10,000 matches is <c>Count = 10000</c>, not capped.</para>
    /// <para>The same on every provider. An implementation that does not override this returns
    /// <see cref="CountLogsAsync"/>'s exact count, never capped.</para>
    /// </remarks>
    async Task<LogCount> CountLogsCappedAsync(LogQuery query, CancellationToken ct) =>
        new(await CountLogsAsync(query, ct), Capped: false);

    /// <summary>
    /// A page of the decisions a run recorded in <c>trax.decision</c> (written when the host calls
    /// <c>AddDecisionRecording</c>), in the order they were recorded, with an answer withheld where
    /// junction events withhold it: see <see cref="RecordedDecisionRecord"/>. A run with no
    /// decisions, or an id with no run, gets an empty page.
    /// </summary>
    /// <remarks>
    /// The run's replay link and the work queue entry's are plain columns
    /// (<c>Metadata.ReplayDecisionsOf</c>, <c>Metadata.ReplayAbandoned</c>,
    /// <c>WorkQueue.ReplayDecisionsOf</c>) and are read with the run and the entry, not here.
    /// </remarks>
    /// <param name="metadataId">The run whose decisions to read.</param>
    /// <param name="afterId">
    /// Keyset cursor: only decisions recorded after this one (a page's
    /// <see cref="RecordedDecisionPage.NextCursor"/>); null for the first page.
    /// </param>
    /// <param name="take">Page size, clamped to 1 through <c>OperationsService.MaxPageSize</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RecordedDecisionPage> GetRecordedDecisionsAsync(
        long metadataId,
        long? afterId,
        int take,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(GetRecordedDecisionsAsync));

    /// <summary>
    /// One work queue entry as an operator reads it, or <c>null</c> when no entry has the id. Its
    /// train input is masked by <see cref="TransportInputRedaction.Redact"/>: each
    /// <c>[TraxSensitive]</c> member reads <c>{"_redacted": true}</c>, and an input this host cannot
    /// read as its type is masked whole. For a queued entry with a subject it also names what the
    /// entry is waiting on: the dispatched entry whose run still holds the subject, or, when
    /// nothing holds it, the queued sibling dispatch would offer first. The dashboard's work queue
    /// entry page and the GraphQL API's <c>workQueue.detail</c> both read it here.
    /// </summary>
    /// <param name="id">The entry's database id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<WorkQueueEntryDetail?> GetWorkQueueEntryDetailAsync(long id, CancellationToken ct) =>
        throw NotImplementedBy(nameof(GetWorkQueueEntryDetailAsync));

    private NotSupportedException NotImplementedBy(string member) =>
        new(
            $"{GetType().Name} does not implement {member}. It was added to IOperationsService "
                + "after this implementation was written."
        );

    /// <summary>
    /// Patches mutable settings on one manifest: enabled, retries, priority, timeout and schedule.
    /// Every check runs before any field is written, so a refused patch changes nothing, and both
    /// the dashboard and the API's <c>updateManifest</c> call it (central <c>docs/0022</c>).
    /// </summary>
    /// <remarks>
    /// Refused: a negative retry count, a priority outside the work queue's range, a timeout or
    /// interval that is not positive, a cron expression the scheduler cannot parse or that never
    /// fires, a switch to <c>Cron</c> without an expression or to <c>Interval</c> without an
    /// interval, and a switch to <c>Once</c>, <c>Dependent</c> or <c>DormantDependent</c>, which
    /// need a time or a parent the patch cannot give. The schedule is checked only when the patch
    /// changes it, so a manifest already holding a bad schedule can still be disabled.
    /// Signals <c>ChangeDomain.Manifest</c> on success.
    /// </remarks>
    /// <param name="id">The manifest's database id.</param>
    /// <param name="update">The fields to change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>OperationResult(true, Id: id, Count: 1, ...)</c> when saved;
    /// <c>OperationResult(false, ...)</c> with the reason when the manifest is not found or the
    /// patch is refused.
    /// </returns>
    Task<OperationResult> UpdateManifestAsync(
        long id,
        ManifestUpdate update,
        CancellationToken ct
    ) => throw NotImplementedBy(nameof(UpdateManifestAsync));

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

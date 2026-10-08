using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunOutcomes;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Trains.JobDispatcher.Junctions;

/// <summary>
/// Creates Metadata records and enqueues each entry to the job submitter.
/// </summary>
/// <remarks>
/// Each entry is dispatched within its own DI scope and database transaction,
/// using <c>FOR UPDATE SKIP LOCKED</c> to atomically claim the work queue entry.
/// This ensures safe concurrent dispatch across multiple server instances.
///
/// When <see cref="SchedulerConfiguration.MaxConcurrentDispatch"/> is greater than 1,
/// entries are dispatched in parallel using a <see cref="SemaphoreSlim"/> to bound concurrency.
/// This is useful for <c>UseRemoteWorkers()</c> where each dispatch blocks on an HTTP POST.
///
/// When <see cref="JobSubmitterRoutingConfiguration"/> is registered, the junction resolves
/// the correct submitter per train based on builder routing or [TraxRemote] attributes.
/// </remarks>
internal class DispatchJobsJunction(
    IServiceProvider serviceProvider,
    ILogger<DispatchJobsJunction> logger,
    JobSubmitterRoutingConfiguration routingConfiguration,
    SchedulerConfiguration schedulerConfiguration,
    ISqlDialect sqlDialect,
    ITrainRegistry trainRegistry,
    ITraxChangeSignal? changeSignal = null
) : EffectJunction<List<WorkQueue>, Unit>
{
    /// <summary>
    /// How long recording a failed submit may take once the dispatcher's own token has been
    /// cancelled.
    /// </summary>
    private static readonly TimeSpan FailureRecordingTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many claims may find an entry's input type unregistered before the entry is settled as
    /// a failed run. Each claim counts as a dispatch attempt and pushes the entry back by the
    /// dispatch backoff (5 s doubling, capped at 5 min), so the entry waits about 20 minutes, the
    /// default stale-pending timeout, for a host that registers the type.
    /// </summary>
    internal const int UnknownInputTypeMaxSkips = 10;

    /// <summary>
    /// How long the lifecycle hooks may take to receive a run that failed at dispatch before the
    /// dispatcher stops waiting for them. Settable so a test can shorten it.
    /// </summary>
    internal static TimeSpan FailurePublishTimeout { get; set; } = FailureRecordingTimeout;

    public override async Task<Unit> Run(List<WorkQueue> entries)
    {
        var dispatchStartTime = DateTime.UtcNow;
        var jobsDispatched = 0;

        logger.LogDebug("Starting DispatchJobsJunction for {EntryCount} entries", entries.Count);

        var maxConcurrent = schedulerConfiguration.MaxConcurrentDispatch;

        if (maxConcurrent <= 1)
        {
            foreach (var entry in entries)
            {
                try
                {
                    var dispatched = await TryClaimAndDispatchAsync(entry);

                    if (dispatched)
                        jobsDispatched++;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Error dispatching work queue entry {WorkQueueId} (train: {TrainName})",
                        entry.Id,
                        entry.TrainName
                    );
                }
            }
        }
        else
        {
            using var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

            var tasks = entries.Select(async entry =>
            {
                await semaphore.WaitAsync(CancellationToken);
                try
                {
                    var dispatched = await TryClaimAndDispatchAsync(entry);

                    if (dispatched)
                        Interlocked.Increment(ref jobsDispatched);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Error dispatching work queue entry {WorkQueueId} (train: {TrainName})",
                        entry.Id,
                        entry.TrainName
                    );
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        var duration = DateTime.UtcNow - dispatchStartTime;

        if (jobsDispatched > 0)
        {
            logger.LogInformation(
                "DispatchJobsJunction completed: {JobsDispatched} jobs dispatched in {Duration}ms",
                jobsDispatched,
                duration.TotalMilliseconds
            );
            changeSignal?.Notify(ChangeDomain.WorkQueue);
        }
        else
            logger.LogDebug("DispatchJobsJunction completed: no jobs dispatched");

        return Unit.Default;
    }

    /// <summary>
    /// The state-machine instance whose invoking state queued the entry, carried to the run so the
    /// run stays linked to it after the instance has left the state; null for every other entry.
    /// </summary>
    private static Trax.Effect.Models.WorkQueue.DTOs.InvokedBy? InvokedBy(WorkQueue claimed) =>
        claimed
            is {
                InvokingMachine: { } machine,
                InvokingInstanceId: { } instance,
                InvokingOwnerKind: { } owner,
            }
            ? new(machine, instance, owner)
            : null;

    /// <summary>
    /// Atomically claims a work queue entry using FOR UPDATE SKIP LOCKED,
    /// creates its Metadata record, and enqueues to the job submitter.
    /// </summary>
    /// <returns>True if the entry was successfully dispatched; false if it was already claimed.</returns>
    private async Task<bool> TryClaimAndDispatchAsync(WorkQueue entry)
    {
        using var scope = serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        using var transaction = await dataContext.BeginTransaction(CancellationToken);

        // Serialize claims for this subject before looking at the entry. Row locking is not enough:
        // two entries for one subject are two different rows, so FOR UPDATE SKIP LOCKED does not
        // make them contend, and while both are still queued neither can see a dispatched sibling
        // to refuse itself. The lock is held for this transaction only, which commits before the
        // job is submitted, so nothing remote happens while it is held.
        if (entry.SubjectKey is not null && dataContext is DbContext database)
        {
            await database.Database.ExecuteSqlRawAsync(
                sqlDialect.LockSubject(),
                [entry.SubjectKey],
                CancellationToken
            );
        }

        // Atomically claim the entry — skips entries locked by other dispatchers, and refuses one
        // whose subject already has a run in flight.
        var claimed = await dataContext
            .WorkQueues.FromSqlRaw(sqlDialect.ClaimWorkQueueEntry(), entry.Id)
            .FirstOrDefaultAsync(CancellationToken);

        if (claimed is null)
        {
            await dataContext.RollbackTransaction();
            logger.LogDebug(
                "Work queue entry {WorkQueueId} already claimed by another server, skipping",
                entry.Id
            );
            return false;
        }

        // Deserialize input if present
        object? deserializedInput = null;
        if (claimed is { Input: not null, InputTypeName: not null })
        {
            // Resolved among the registered trains' inputs only.
            var inputType = RegisteredInputTypes.Find(trainRegistry, claimed.InputTypeName);

            if (inputType is null)
            {
                // Not an input of any train registered on this host. Another host may know it (a
                // rolling deploy, or hosts scanning different assemblies), so the entry is left
                // for one that does, and settled only once it has gone unread for long enough.
                if (claimed.DispatchAttempts + 1 < UnknownInputTypeMaxSkips)
                {
                    await DeferUnknownInputTypeAsync(dataContext, claimed);
                    return false;
                }

                await RecordUnreadableInputAsync(
                    dataContext,
                    claimed,
                    new TrainException(
                        $"'{claimed.InputTypeName}' is not the input of any registered train on "
                            + $"any host that claimed it in {UnknownInputTypeMaxSkips} attempts."
                    )
                );
                return false;
            }

            try
            {
                deserializedInput = JsonSerializer.Deserialize(
                    claimed.Input,
                    inputType,
                    TraxJsonSerializationOptions.ManifestProperties
                );
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                await RecordUnreadableInputAsync(dataContext, claimed, ex);
                return false;
            }
        }

        await DropReplayLinkIfManifestOptedOutAsync(dataContext, claimed);
        await DropReplayLinkIfAlreadyReplayedAsync(dataContext, claimed);

        // Create a new Metadata record for this execution.
        // Propagate the WorkQueue's ExternalId so clients can correlate the queue
        // mutation response with subscription events (both use the same externalId).
        var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
            new CreateMetadata
            {
                Name = claimed.TrainName,
                ExternalId = claimed.ExternalId,
                Input = null,
                ManifestId = claimed.ManifestId,
                ReplayDecisionsOf = claimed.ReplayDecisionsOf,
                InvokedBy = InvokedBy(claimed),
            }
        );

        await dataContext.Track(metadata);
        await dataContext.SaveChanges(CancellationToken);

        // Update work queue entry
        claimed.Status = WorkQueueStatus.Dispatched;
        claimed.MetadataId = metadata.Id;
        // By the database's clock: a dependent's next run is decided by comparing this with its
        // parent's LastSuccessfulRun, which a worker on another machine stamps (see DatabaseClock).
        claimed.DispatchedAt = await DatabaseClock.UtcNowAsync(
            dataContext.WorkQueues.Where(q => q.Id == claimed.Id),
            CancellationToken
        );
        await dataContext.SaveChanges(CancellationToken);

        // Link retry metadata on the dead letter if this WorkQueue was from a requeue
        await LinkDeadLetterRetryAsync(dataContext, claimed, metadata.Id);

        // Commit the claim transaction before enqueuing. The Metadata and WorkQueue
        // updates must be visible to the job submitter — InMemoryJobSubmitter executes
        // synchronously and needs to read the Metadata, while PostgresJobSubmitter and
        // other submitters need the committed state to be visible.
        await dataContext.CommitTransaction();

        // Resolve the correct submitter for this train (routed or default)
        var jobSubmitter = ResolveSubmitter(scope.ServiceProvider, claimed.TrainName);

        // Enqueue to job submitter (outside the transaction).
        // If this fails, the Metadata is already committed — mark it as Failed
        // immediately so it doesn't stay orphaned in Pending state forever.
        try
        {
            string backgroundTaskId;
            if (deserializedInput != null)
                backgroundTaskId = await jobSubmitter.EnqueueAsync(
                    metadata.Id,
                    deserializedInput,
                    claimed.Priority,
                    CancellationToken
                );
            else
                backgroundTaskId = await jobSubmitter.EnqueueAsync(
                    metadata.Id,
                    claimed.Priority,
                    CancellationToken
                );

            logger.LogDebug(
                "Dispatched work queue entry {WorkQueueId} as background task {BackgroundTaskId} (Metadata: {MetadataId})",
                entry.Id,
                backgroundTaskId,
                metadata.Id
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to enqueue work queue entry {WorkQueueId} (Metadata: {MetadataId}). Handling dispatch failure",
                entry.Id,
                metadata.Id
            );

            await HandleDispatchFailureAsync(entry.Id, metadata.Id, ex);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Drops the replay link of a manifest's entry when the manifest no longer replays decisions
    /// on retry. The link was set when the retry was queued; the flag may have been turned off
    /// while it waited out its backoff, and the run then asks afresh (docs/adr/0017).
    /// </summary>
    private async Task DropReplayLinkIfManifestOptedOutAsync(
        IDataContext dataContext,
        WorkQueue claimed
    )
    {
        if (claimed.ReplayDecisionsOf is null || claimed.ManifestId is not { } manifestId)
            return;

        var replays = await dataContext
            .Manifests.AsNoTracking()
            .Where(m => m.Id == manifestId)
            .Select(m => (bool?)m.ReplayDecisionsOnRetry)
            .FirstOrDefaultAsync(CancellationToken);

        if (replays != false)
            return;

        logger.LogInformation(
            "Work queue entry {WorkQueueId} no longer replays run {ReplayDecisionsOf}: manifest "
                + "{ManifestId} stopped replaying decisions on retry after it was queued",
            claimed.Id,
            claimed.ReplayDecisionsOf,
            manifestId
        );
        claimed.ReplayDecisionsOf = null;
    }

    /// <summary>
    /// Drops the replay link of an entry whose source run's answers a run already replays, so the
    /// entry's run asks afresh: a run's answers are replayed at most once (docs/adr/0017).
    /// </summary>
    /// <remarks>
    /// The checks made when an entry is queued read the runs and the queued entries, and the
    /// database's unique index holds one <em>queued</em> replay per run. Neither sees a replay that
    /// was dispatched after the check, so a second entry naming the same run can still be queued
    /// once the first has left the queue. This is the check that holds the rule, because every
    /// linked run is created here.
    /// <para>
    /// Two dispatchers can claim two such entries at once, and neither would see the other's run
    /// until it commits. So every linked claim first writes the source run's row (setting a column
    /// to its own value), which serializes them on that row until the claim transaction commits,
    /// and only then looks for a run replaying it. The row lock belongs to that run alone, so it
    /// contends with nothing but another dispatch of the same source, and it needs no
    /// provider-specific SQL: on a single-writer provider the write takes the database's write lock
    /// before the read, which serializes the same way. A source already deleted has no row to
    /// lock; the run then finds nothing to replay and asks afresh anyway.
    /// </para>
    /// <para>
    /// A run abandoned its replay asked afresh, and a dispatch attempt that failed and was requeued
    /// (<see cref="DispatchFailure.Requeued"/>) never ran; the entry it belongs to is the one being
    /// dispatched again, so neither counts as the replay.
    /// </para>
    /// </remarks>
    private async Task DropReplayLinkIfAlreadyReplayedAsync(
        IDataContext dataContext,
        WorkQueue claimed
    )
    {
        if (claimed.ReplayDecisionsOf is not { } source)
            return;

        await dataContext
            .Metadatas.Where(m => m.Id == source)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ReplayAbandoned, m => m.ReplayAbandoned),
                CancellationToken
            );

        var replayedBy = await dataContext
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.ReplayDecisionsOf == source
                && !m.ReplayAbandoned
                && m.FailureException != DispatchFailure.Requeued
            )
            .Select(m => (long?)m.Id)
            .FirstOrDefaultAsync(CancellationToken);

        if (replayedBy is null)
            return;

        logger.LogInformation(
            "Work queue entry {WorkQueueId} no longer replays run {ReplayDecisionsOf}: run "
                + "{ReplayedBy} already replays its decisions, so this run asks afresh",
            claimed.Id,
            source,
            replayedBy
        );
        claimed.ReplayDecisionsOf = null;
    }

    /// <summary>
    /// Returns an entry whose input type this host does not register to the queue, inside the
    /// claim transaction: counts the attempt and pushes its <c>ScheduledAt</c> back by the
    /// dispatch backoff. No run is recorded, so nothing counts toward the manifest's retries.
    /// </summary>
    private async Task DeferUnknownInputTypeAsync(IDataContext dataContext, WorkQueue claimed)
    {
        var attempts = claimed.DispatchAttempts + 1;
        var backoff = DispatchFailure.Backoff(attempts);

        claimed.DispatchAttempts = attempts;
        // By the database's clock, which the dispatcher compares it with (see DatabaseClock).
        claimed.ScheduledAt =
            await DatabaseClock.UtcNowAsync(
                dataContext.WorkQueues.Where(q => q.Id == claimed.Id),
                CancellationToken
            ) + backoff;
        await dataContext.SaveChanges(CancellationToken);
        await dataContext.CommitTransaction();

        logger.LogWarning(
            "Work queue entry {WorkQueueId} (train: {TrainName}) has input type {InputType}, which "
                + "no train registered on this host takes; left it queued for a host that does "
                + "(attempt {Attempt} of {MaxAttempts}, next in {Backoff})",
            claimed.Id,
            claimed.TrainName,
            claimed.InputTypeName,
            attempts,
            UnknownInputTypeMaxSkips,
            backoff
        );
    }

    /// <summary>
    /// Settles an entry whose stored input cannot be read, inside the claim transaction: records a
    /// Failed run for it with the reason and marks the entry Dispatched to that run. An entry from
    /// a dead-letter retry has the failed run linked as the dead letter's retry run.
    /// </summary>
    /// <remarks>
    /// JSON that no longer fits its type will not be readable on any later cycle or any other
    /// host, so it is settled at once. An input type no host has registered after
    /// <see cref="UnknownInputTypeMaxSkips"/> claims is settled the same way. Left Queued, the
    /// entry kept its manifest from scheduling and failed again every cycle. Settled, it is a
    /// failure of its manifest like any other, so retries and dead-lettering apply.
    /// </remarks>
    private async Task RecordUnreadableInputAsync(
        IDataContext dataContext,
        WorkQueue claimed,
        Exception exception
    )
    {
        await DropReplayLinkIfManifestOptedOutAsync(dataContext, claimed);
        await DropReplayLinkIfAlreadyReplayedAsync(dataContext, claimed);

        var failure = new TrainException(
            $"The input of work queue entry {claimed.Id} could not be read as "
                + $"'{claimed.InputTypeName}', so it was not dispatched: {exception.Message}"
        );

        var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
            new CreateMetadata
            {
                Name = claimed.TrainName,
                ExternalId = claimed.ExternalId,
                Input = null,
                ManifestId = claimed.ManifestId,
                ReplayDecisionsOf = claimed.ReplayDecisionsOf,
                InvokedBy = InvokedBy(claimed),
            }
        );
        metadata.TrainState = TrainState.Failed;
        metadata.EndTime = DateTime.UtcNow;
        metadata.AddException(failure);

        await dataContext.Track(metadata);
        await dataContext.SaveChanges(CancellationToken);

        claimed.Status = WorkQueueStatus.Dispatched;
        claimed.MetadataId = metadata.Id;
        // By the database's clock: a dependent's next run is decided by comparing this with its
        // parent's LastSuccessfulRun, which a worker on another machine stamps (see DatabaseClock).
        claimed.DispatchedAt = await DatabaseClock.UtcNowAsync(
            dataContext.WorkQueues.Where(q => q.Id == claimed.Id),
            CancellationToken
        );
        await dataContext.SaveChanges(CancellationToken);
        await LinkDeadLetterRetryAsync(dataContext, claimed, metadata.Id);
        await dataContext.CommitTransaction();
        await PublishFailedAsync(metadata.Id);

        logger.LogError(
            exception,
            "Work queue entry {WorkQueueId} (train: {TrainName}) has an input that cannot be read "
                + "as {InputType}; recorded it as failed run {MetadataId} and did not dispatch it",
            claimed.Id,
            claimed.TrainName,
            claimed.InputTypeName,
            metadata.Id
        );
    }

    /// <summary>
    /// Records <paramref name="metadataId"/> as the retry run of the dead letter the entry was
    /// requeued from, if it was.
    /// </summary>
    private async Task LinkDeadLetterRetryAsync(
        IDataContext dataContext,
        WorkQueue claimed,
        long metadataId
    )
    {
        if (claimed.DeadLetterId is null)
            return;

        var deadLetter = await dataContext.DeadLetters.FirstOrDefaultAsync(
            d => d.Id == claimed.DeadLetterId,
            CancellationToken
        );

        if (deadLetter is null)
            return;

        deadLetter.LinkRetryMetadata(metadataId);
        await dataContext.SaveChanges(CancellationToken);

        logger.LogDebug(
            "Linked retry metadata {MetadataId} to dead letter {DeadLetterId}",
            metadataId,
            claimed.DeadLetterId
        );
    }

    /// <summary>
    /// Handles a submitter failure: when the job was not delivered, records the attempt as Failed
    /// and requeues the entry if attempts remain; when the runner already started it, leaves the
    /// run to the runner.
    /// </summary>
    /// <remarks>
    /// A submitter that throws has not always failed to deliver. A runner that ran the job and
    /// reported its failure, or that is still running it when the HTTP call times out, already
    /// owns the run, and its outcome is (or will be) recorded on the run's row. Only a row that is
    /// still <c>Pending</c> is a job no runner started, so the row is failed with a conditional
    /// write that matches only while it is still <c>Pending</c>. If that write matches nothing,
    /// the job was delivered: the entry stays Dispatched and no attempt is counted. If it matches,
    /// a runner that receives the job later loses its claim to the Failed row and does not run it,
    /// so a requeued entry cannot run twice.
    /// <para>
    /// If <see cref="SchedulerConfiguration.MaxDispatchAttempts"/> is greater than 0 and the entry
    /// has not exhausted its attempts, the entry is reset to Queued so a later cycle creates a new
    /// Metadata and retries. The failed Metadata stays as an immutable audit record.
    /// </para>
    /// </remarks>
    private async Task HandleDispatchFailureAsync(
        long workQueueId,
        long metadataId,
        Exception exception
    )
    {
        // Not the dispatcher's token: that is the host's stopping token, and a shutdown is exactly
        // when a submit fails because it was cancelled. The claim is already committed, so this is
        // the only write that records the attempt and frees the entry; on a cancelled token it
        // never landed, and the entry held its subject until the stale-pending reaper ran. Bounded
        // rather than uncancellable so an unreachable database cannot hold shutdown open
        // (effect/0005 records the same rule for a train's outcome).
        using var bounded = new CancellationTokenSource(FailureRecordingTimeout);
        var token = bounded.Token;
        var failedForGood = false;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

            using var transaction = await dataContext.BeginTransaction(token);

            var workQueueEntry = await dataContext.WorkQueues.FirstOrDefaultAsync(
                w => w.Id == workQueueId,
                token
            );

            // An entry with attempts left is requeued, and its failed run is marked so it does
            // not count as a failure of the manifest (see DispatchFailure).
            var maxAttempts = schedulerConfiguration.MaxDispatchAttempts;
            var attempts = (workQueueEntry?.DispatchAttempts ?? 0) + 1;
            var requeue = maxAttempts > 0 && workQueueEntry is not null && attempts < maxAttempts;

            // 1. Fail the run only if no runner has started it.
            var failure = DescribeFailure(exception);
            var failureException = requeue ? DispatchFailure.Requeued : failure.FailureException;
            var failureReason = requeue
                ? $"Dispatch attempt {attempts} of {maxAttempts} failed and the job was requeued: "
                    + $"{failure.FailureException}: {failure.FailureReason}"
                : failure.FailureReason;

            var now = DateTime.UtcNow;
            var failed = await dataContext
                .Metadatas.Where(m => m.Id == metadataId && m.TrainState == TrainState.Pending)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(m => m.TrainState, TrainState.Failed)
                            .SetProperty(m => m.EndTime, now)
                            .SetProperty(m => m.FailureException, failureException)
                            .SetProperty(m => m.FailureReason, failureReason)
                            .SetProperty(m => m.FailureJunction, nameof(DispatchJobsJunction))
                            .SetProperty(m => m.StackTrace, failure.StackTrace)
                            .SetProperty(m => m.FailureClass, failure.FailureClass),
                    token
                );

            if (failed == 0)
            {
                await dataContext.RollbackTransaction();
                logger.LogWarning(
                    exception,
                    "The submitter reported a failure for work queue entry {WorkQueueId}, but a runner "
                        + "has already started Metadata {MetadataId}; the run's outcome is the runner's "
                        + "to record, so the entry is not requeued",
                    workQueueId,
                    metadataId
                );
                return;
            }

            // 2. Requeue the work queue entry if attempts remain, after a backoff
            if (maxAttempts > 0 && workQueueEntry is not null)
            {
                workQueueEntry.DispatchAttempts = attempts;

                if (requeue)
                {
                    var backoff = DispatchFailure.Backoff(attempts);
                    workQueueEntry.Status = WorkQueueStatus.Queued;
                    workQueueEntry.MetadataId = null;
                    workQueueEntry.DispatchedAt = null;
                    // By the database's clock, which the dispatcher compares it with (see
                    // DatabaseClock).
                    workQueueEntry.ScheduledAt =
                        await DatabaseClock.UtcNowAsync(
                            dataContext.WorkQueues.Where(w => w.Id == workQueueId),
                            token
                        ) + backoff;

                    logger.LogWarning(
                        "Requeued work queue entry {WorkQueueId} after dispatch failure "
                            + "(attempt {Attempt}/{MaxAttempts}); next attempt in {Backoff}",
                        workQueueId,
                        attempts,
                        maxAttempts,
                        backoff
                    );
                }
                else
                {
                    logger.LogError(
                        "Work queue entry {WorkQueueId} exhausted dispatch attempts "
                            + "({Attempts}/{MaxAttempts}). Leaving as Dispatched for dead letter handling",
                        workQueueId,
                        attempts,
                        maxAttempts
                    );
                }
            }

            await dataContext.SaveChanges(token);
            await dataContext.CommitTransaction();
            failedForGood = !requeue;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to handle dispatch failure for work queue entry {WorkQueueId} "
                    + "(Metadata: {MetadataId}). The ReapStalePendingMetadataJunction will recover it "
                    + "on the next ManifestManager cycle",
                workQueueId,
                metadataId
            );
        }

        // A run failed for good is the run's terminal outcome, published as a train's own failure
        // is. A requeued attempt is not: its entry runs again under the same external id. Nor is
        // a row no longer Pending, which a runner owns and reports itself.
        if (failedForGood)
            await PublishFailedAsync(metadataId);
    }

    /// <summary>
    /// Publishes the Failed outcome of a run the dispatcher failed before any runner started it,
    /// through the same lifecycle hooks a train's own failure goes through: <c>OnFailed</c>, then
    /// <c>OnStateChanged</c>, which is what the broadcaster and the GraphQL subscriptions follow.
    /// No train ran, so nothing else publishes it, and a client following the queued run by its
    /// external id would otherwise wait forever.
    /// </summary>
    /// <remarks>
    /// Called once, after the Failed row is committed. The hooks swallow their own failures; a
    /// failure to read the row is logged, since the outcome is already recorded either way.
    /// <para>
    /// The hooks are given the run with <see cref="DispatchFailure.Published"/> as its failure (see
    /// <see cref="DispatchFailure.PublishedAndShown"/>), not
    /// what the row records. A dispatch failure's detail is the dispatcher's and the runner's (a
    /// remote worker's response body, an input type's assembly-qualified name, a serializer's
    /// message), and a lifecycle event reaches subscribers who are not operators. The row keeps the
    /// full detail for the operations queries.
    /// </para>
    /// <para>
    /// The dispatch loop waits for this, so the hooks are bounded by
    /// <see cref="FailurePublishTimeout"/>: a hook that hangs, whether or not it honours its
    /// token, is abandoned and logged rather than holding up every entry behind it.
    /// </para>
    /// </remarks>
    private Task PublishFailedAsync(long metadataId) =>
        OutOfTrainOutcomes.PublishFailedAsync(
            serviceProvider,
            [metadataId],
            logger,
            DispatchFailure.PublishedAndShown,
            FailurePublishTimeout
        );

    /// <summary>
    /// The failure fields <see cref="Trax.Effect.Models.Metadata.Metadata.AddException"/> would
    /// record for <paramref name="exception"/>, for a write that sets them without loading the row.
    /// </summary>
    private static Trax.Effect.Models.Metadata.Metadata DescribeFailure(Exception exception)
    {
        var description = Trax.Effect.Models.Metadata.Metadata.Create(
            new CreateMetadata
            {
                Name = nameof(DispatchJobsJunction),
                ExternalId = string.Empty,
                Input = null,
            }
        );
        description.AddException(exception);
        return description;
    }

    /// <summary>
    /// Resolves the appropriate job submitter for a train based on routing configuration.
    /// Falls back to the default IJobSubmitter if no routing is configured for this train.
    /// </summary>
    private IJobSubmitter ResolveSubmitter(IServiceProvider provider, string trainName) =>
        routingConfiguration.ResolveSubmitter(provider, trainName)
        ?? provider.GetRequiredService<IJobSubmitter>();
}

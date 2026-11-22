using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Applies the outcome of a run a machine state invoked to the one row whose <c>invoke_token</c> is the run's
/// external id, exactly once, by a conditional update. The lifecycle hook (on the host that ran the train) and the
/// reconciler (on every host that registers machines) both deliver through it; any number of deliveries of one run,
/// from any number of hosts, apply it once, because only the first conditional update matches.
/// </summary>
/// <remarks>
/// <para>The run's end is read from the store, never from the caller: a queued entry marked cancelled is
/// Cancelled; a dispatched entry's run is Done, Failed or Cancelled once its row says so. A Done run's output is
/// the copy the run wrote for the machine in its own terminal write (<c>metadata.invoke_output</c>), never
/// <c>metadata.output</c>. A dispatch that failed and was requeued is not an end: its entry is queued again.</para>
/// <para>Fail-closed: a Done outcome that cannot be applied (its output too large, unaccepted by every
/// <c>OnDone</c> guard, unrecorded, or reduced to a refused context, or its target's run refused) is applied as the
/// state's <c>OnFailed</c> with a typed reason, so the instance never waits on a run that has finished. So is a run
/// that can no longer be found (<see cref="InvokeOutcomeReasons.RunMissing"/>), so the instance never waits on a
/// run whose records were deleted.</para>
/// See <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </remarks>
internal sealed class InvokeOutcomeDelivery(
    IDataContext context,
    IMachineInstanceStore store,
    InvokeOutbox outbox,
    IEnumerable<IMachine> machines,
    ILogger<InvokeOutcomeDelivery> logger
)
{
    // A row whose concurrency token moves between the read and the write (an ordinary self-loop advance of the
    // state) is read again; past this many tries the next sweep takes it.
    private const int Attempts = 5;

    /// <summary>
    /// The <c>failure_exception</c> the scheduler's dispatcher gives the run of a dispatch that failed and was
    /// requeued (Trax.Scheduler's <c>DispatchFailure.Requeued</c>). Such a run never started: its entry is queued
    /// again and a later dispatch records the run that carries the outcome, so it is never an end.
    /// </summary>
    internal const string RequeuedDispatch = "DispatchRequeued";

    private static readonly TrainState[] Terminal =
    [
        TrainState.Completed,
        TrainState.Failed,
        TrainState.Cancelled,
    ];

    private readonly Dictionary<string, IMachine> _machines = RegisteredMachines
        .Distinct(machines)
        .ToDictionary(m => m.Name, StringComparer.Ordinal);

    /// <summary>Delivers the outcome of the run whose external id is <paramref name="invokeToken"/>.</summary>
    public async Task<InvokeDelivery> Deliver(
        string invokeToken,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(invokeToken);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var row = await store
                .GetByInvokeToken(invokeToken, cancellationToken)
                .ConfigureAwait(false);
            if (row is null)
                return new InvokeDelivery.NoTransition();

            if (
                !_machines.TryGetValue(row.Machine, out var found)
                || found is not IMachineInternals machine
            )
                return new InvokeDelivery.NotHere(row.Machine);

            if (ReadCurrent(machine, row, invokeToken) is not { } current)
                return new InvokeDelivery.NotHere(row.Machine);

            InvokedRunEnd? end;
            if (
                machine.Spawns.Any(s =>
                    string.Equals(s.State, current.State, StringComparison.Ordinal)
                )
            )
            {
                var (childEnd, childHere) = await ReadChildEnd(
                        machine,
                        current.State,
                        invokeToken,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (childHere is not null)
                    return new InvokeDelivery.NotHere(childHere);
                end = childEnd;
            }
            else
                end = await ReadEnd(invokeToken, cancellationToken).ConfigureAwait(false);
            if (end is null)
                return new InvokeDelivery.Running();

            var result = await Apply(row, machine, current, invokeToken, end, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
                return result;
        }

        return new InvokeDelivery.Contended();
    }

    // The row's snapshot as this host reads it; null when another host must apply the outcome.
    private Snapshot? ReadCurrent(IMachineInternals machine, StoredInstance row, string invokeToken)
    {
        switch (machine.Rehydrate(row.Snapshot.Json))
        {
            case RehydrationResult.Ok ok:
                return ok.Snapshot;
            case RehydrationResult.Error error:
                // Another host, with the definition this row was written under, can read it.
                logger.LogWarning(
                    error.Exception,
                    "The outcome of run {RunId} cannot be applied to {Machine} instance {InstanceId} on this "
                        + "host, which cannot read the stored snapshot ({Code}).",
                    invokeToken,
                    row.Machine,
                    row.Id,
                    error.Code.ToWire()
                );
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// One page of the live tokens of <paramref name="machines"/> whose runs have ended, in token order after
    /// <paramref name="after"/>, restricted to <paramref name="among"/> when it is given. The reconciler's sweep
    /// pages through these, and its notices are filtered through them, so a run still going costs no delivery.
    /// </summary>
    /// <remarks>
    /// One statement reads each row with its entry and the entry's run, so a token is ended or not as of one
    /// instant, and only rows of the machines named are read. A token whose entry the queue no longer holds is
    /// settled by a second read of the runs under its external id, which happens only after records were deleted.
    /// <see cref="EndedPage.Next"/> is where the next page starts, null after the last.
    /// </remarks>
    public async Task<EndedPage> Ended(
        IReadOnlyCollection<string> machines,
        int limit,
        string? after = null,
        IReadOnlyCollection<string>? among = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (machines.Count == 0 || among is { Count: 0 })
            return new EndedPage([], Next: null);

        var drafts = WaitingDrafts(machines, after, among);

        // A row is a candidate when its run has ended by its entry and the entry's run, or when its entry is
        // gone and only the runs under its token can say. Ordered by the token, which the partial unique index
        // on it serves, so each page reads on from where the last one stopped.
        var rows = await (
            from d in drafts
            join w in context.WorkQueues.AsNoTracking()
                on d.InvokeToken equals w.ExternalId
                into entries
            from w in entries.DefaultIfEmpty()
            join m in context.Metadatas.AsNoTracking() on w.MetadataId equals (long?)m.Id into runs
            from m in runs.DefaultIfEmpty()
            where
                w == null
                || w.Status == WorkQueueStatus.Cancelled
                || (
                    w.Status == WorkQueueStatus.Dispatched
                    && (
                        w.MetadataId == null
                        || (
                            Terminal.Contains(m.TrainState)
                            && !(
                                m.TrainState == TrainState.Failed
                                && m.FailureException == RequeuedDispatch
                            )
                        )
                    )
                )
            orderby d.InvokeToken
            select new { Token = d.InvokeToken.NotNull(), Unqueued = w == null }
        )
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var next = rows.Count == limit ? rows[^1].Token : null;
        var unqueued = rows.Where(r => r.Unqueued).Select(r => r.Token).ToList();
        if (unqueued.Count == 0)
            return new EndedPage(rows.Select(r => r.Token).ToList(), next);

        var latest = await LatestRunStates(unqueued, cancellationToken).ConfigureAwait(false);

        var ended = rows.Where(r =>
                !r.Unqueued
                || !latest.TryGetValue(r.Token, out var state)
                || Terminal.Contains(state)
            )
            .Select(r => r.Token)
            .ToList();
        return new EndedPage(ended, next);
    }

    private IQueryable<Trax.Effect.Models.SnapshotDraft.SnapshotDraft> WaitingDrafts(
        IReadOnlyCollection<string> machines,
        string? after,
        IReadOnlyCollection<string>? among
    )
    {
        var names = machines.ToList();
        // A token a child carries is a spawn still waiting, which the sweep of child ends delivers; one whose child is
        // gone reads like a run whose records were, and is delivered here, as the child's cancel.
        var children = context.SnapshotDrafts;
        var drafts = context
            .SnapshotDrafts.AsNoTracking()
            .Where(d =>
                d.InvokeToken != null
                && names.Contains(d.Machine)
                && !children.Any(c => c.SpawnedBy == d.InvokeToken)
            );
        if (among is not null)
        {
            var tokens = among.ToList();
            drafts = drafts.Where(d => d.InvokeToken != null && tokens.Contains(d.InvokeToken));
        }
        if (after is not null)
            drafts = drafts.Where(d => string.Compare(d.InvokeToken, after) > 0);
        return drafts;
    }

    private async Task<Dictionary<string, TrainState>> LatestRunStates(
        List<string> unqueued,
        CancellationToken cancellationToken
    )
    {
        // The latest run under each token that is not a requeued dispatch; a token with none has ended as far as
        // its machine can tell, and the delivery applies it as a failure (see ReadEnd).
        return (
            await context
                .Metadatas.AsNoTracking()
                .Where(m =>
                    unqueued.Contains(m.ExternalId)
                    && !(
                        m.TrainState == TrainState.Failed && m.FailureException == RequeuedDispatch
                    )
                )
                .Select(m => new
                {
                    m.Id,
                    m.ExternalId,
                    m.TrainState,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
        ).GroupBy(m => m.ExternalId.Trim(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.MaxBy(m => m.Id).NotNull().TrainState, StringComparer.Ordinal);
    }

    /// <summary>
    /// One page of the tokens of parents still waiting on a child that has entered one of its end states, in token
    /// order after <paramref name="after"/>. <paramref name="ends"/> names, per child machine, the states it ends in.
    /// The reconciler's sweep delivers these, so a child's end that the write which made it did not deliver still
    /// reaches its parent.
    /// </summary>
    public async Task<EndedPage> ChildrenEnded(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ends,
        int limit,
        string? after = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var keys = ends.SelectMany(kv =>
                kv.Value.Select(state => kv.Key + DraftExpiry.Separator + state)
            )
            .ToList();
        if (keys.Count == 0)
            return new EndedPage([], Next: null);

        var parents = context.SnapshotDrafts;
        var children = context
            .SnapshotDrafts.AsNoTracking()
            .Where(c =>
                c.SpawnedBy != null
                && keys.Contains(c.Machine + DraftExpiry.Separator + c.State)
                && parents.Any(p => p.InvokeToken == c.SpawnedBy)
            );
        if (after is not null)
            children = children.Where(c => string.Compare(c.SpawnedBy, after) > 0);
        var tokens = await children
            .OrderBy(c => c.SpawnedBy)
            .Take(limit)
            .Select(c => c.SpawnedBy.NotNull())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new EndedPage(tokens, tokens.Count == limit ? tokens[^1] : null);
    }

    /// <summary>
    /// Delivers the end of the instance <paramref name="id"/> of <paramref name="machine"/> to the parent that spawned
    /// it, when it is in one of its end states and its parent still waits; otherwise does nothing. Called after a write
    /// that may have ended a child, as the fast path the sweep backs.
    /// </summary>
    public async Task<InvokeDelivery?> DeliverEndOf(
        DraftOwner owner,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !_machines.TryGetValue(machine, out var found)
            || found is not IMachineInternals { Ends.Count: > 0 } child
        )
            return null;

        var row = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d =>
                d.Id == id
                && d.Machine == machine
                && d.OwnerKind == owner.Kind
                && d.UserKey == owner.UserKey
                && d.SpawnedBy != null
            )
            .Select(d => new { d.State, d.SpawnedBy })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is { SpawnedBy: { } parentToken } && child.Ends.ContainsKey(row.State)
            ? await Deliver(parentToken, cancellationToken).ConfigureAwait(false)
            : null;
    }

    // How the child the parent's spawning state started has ended, or null while it has not; the second value names
    // the child's machine when this host does not register it and cannot tell. A child that is gone was cancelled.
    private async Task<(InvokedRunEnd? End, string? NotHere)> ReadChildEnd(
        IMachineInternals parent,
        string state,
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        var child = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.SpawnedBy == invokeToken)
            .Select(d => new
            {
                d.Machine,
                d.State,
                d.Context,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (child is null)
            return (
                new InvokedRunEnd(InvokeOutcomeKind.Cancelled, Output: null, Oversize: false),
                null
            );
        if (
            !_machines.TryGetValue(child.Machine, out var found)
            || found is not IMachineInternals childMachine
        )
            return (null, child.Machine);
        if (!childMachine.Ends.TryGetValue(child.State, out var kind))
            return (null, null);
        if (kind != InvokeOutcomeKind.Done)
            return (new InvokedRunEnd(kind, Output: null, Oversize: false), null);

        var childContext = JsonNode.Parse(child.Context) as JsonObject ?? [];
        return (
            new InvokedRunEnd(
                InvokeOutcomeKind.Done,
                parent.ChildOutput(state, childContext).ToJsonString(),
                Oversize: false
            ),
            null
        );
    }

    /// <summary>How the run <paramref name="invokeToken"/> names ended, or null while it has not.</summary>
    internal async Task<InvokedRunEnd?> ReadEnd(
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        // The entry and the run it names in one statement, so they are read as of one instant. Read in two, a
        // failed dispatch that is requeued between them (it fails the run and queues the entry again in one
        // transaction) would show the entry dispatched and the run failed, an end that never happened.
        var entry = await context
            .WorkQueues.AsNoTracking()
            .Where(w => w.ExternalId == invokeToken)
            .OrderByDescending(w => w.Id)
            .Select(w => new
            {
                w.Status,
                w.MetadataId,
                // Every column of the run is null when the entry names none; the join is a left join.
                State = w.Metadata == null ? null : (TrainState?)w.Metadata.TrainState,
                FailureException = w.Metadata == null ? null : w.Metadata.FailureException,
                InvokeOutput = w.Metadata == null ? null : w.Metadata.InvokeOutput,
                Oversize = w.Metadata == null ? null : (bool?)w.Metadata.InvokeOutputOversize,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        switch (entry)
        {
            // Neither the entry nor the run exists. That is never a run still to come: the outbox writes the
            // entry in the same transaction that sets the token, and every caller reads the token (the row that
            // holds it) before this, so an entry that was ever written is visible here unless it was deleted.
            // Dispatch only updates an entry, and no cleanup deletes an invoked run's entry on its own: it has no
            // manifest (the manifest pruner), is never staged (the stranded-entry sweep) and never dead-lettered
            // (dead letter cleanup). Metadata retention deletes a finished run together with its entry, and keeps
            // an invoked run while an instance still holds its token. So this is a run whose records were deleted
            // some other way; it can never end, so the machine treats it as failed.
            case null:
                return await ReadEndByToken(invokeToken, cancellationToken).ConfigureAwait(false);
            // Never dispatched: cancelled while queued, by leaving the state, an operator, or a draft's expiry.
            case { Status: WorkQueueStatus.Cancelled }:
                return new InvokedRunEnd(
                    InvokeOutcomeKind.Cancelled,
                    Output: null,
                    Oversize: false
                );
            // Not yet dispatched, or requeued after a failed dispatch: the run is still to come.
            case { Status: WorkQueueStatus.Queued }:
                return null;
            // Dispatched, but naming no run: none can be read through it, so the runs under the token are all
            // there is, and with none the run can never end.
            case { MetadataId: null }:
                return await ReadEndByToken(invokeToken, cancellationToken).ConfigureAwait(false);
        }

        // A run failed with the requeue mark is a dispatch attempt, not the run: its entry is queued again.
        if (
            entry.State == TrainState.Failed
            && string.Equals(entry.FailureException, RequeuedDispatch, StringComparison.Ordinal)
        )
            return null;

        return End(entry.State, entry.InvokeOutput, entry.Oversize ?? false);
    }

    // The end of the latest run recorded under the token that is not a requeued dispatch, for a token whose entry
    // says nothing; a token with no such run can never end, and is a failure (RunMissing).
    private async Task<InvokedRunEnd?> ReadEndByToken(
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        var run = await context
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.ExternalId == invokeToken
                && !(m.TrainState == TrainState.Failed && m.FailureException == RequeuedDispatch)
            )
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.TrainState,
                m.InvokeOutput,
                m.InvokeOutputOversize,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return run is null
            ? new InvokedRunEnd(
                InvokeOutcomeKind.Failed,
                Output: null,
                Oversize: false,
                InvokeOutcomeReasons.RunMissing
            )
            : End(run.TrainState, run.InvokeOutput, run.InvokeOutputOversize);
    }

    private static InvokedRunEnd? End(TrainState? state, string? output, bool oversize) =>
        state switch
        {
            TrainState.Completed => new InvokedRunEnd(InvokeOutcomeKind.Done, output, oversize),
            TrainState.Failed => new InvokedRunEnd(
                InvokeOutcomeKind.Failed,
                Output: null,
                Oversize: false
            ),
            TrainState.Cancelled => new InvokedRunEnd(
                InvokeOutcomeKind.Cancelled,
                Output: null,
                Oversize: false
            ),
            _ => null,
        };

    // One attempt: the run's own outcome, else (for a Done that cannot be applied) its OnFailed, else the token
    // cleared. Null when the conditional update lost to a concurrent change of the row that kept the token.
    private async Task<InvokeDelivery?> Apply(
        StoredInstance row,
        IMachineInternals machine,
        Snapshot current,
        string invokeToken,
        InvokedRunEnd end,
        CancellationToken cancellationToken
    )
    {
        var outcome = OutcomeOf(end, out var reason);

        while (true)
        {
            var planned = Plan(machine, current, outcome, row, out var problem);
            if (planned is not null)
            {
                var entering = machine.Entering(planned.State);
                var write = await outbox
                    .Deliver(
                        row.Owner,
                        row.Id,
                        invokeToken,
                        planned,
                        row.Snapshot.Token,
                        entering,
                        cancellationToken,
                        $"{current.State}.{OutcomeTriggers.Suffix(outcome.Kind)}"
                    )
                    .ConfigureAwait(false);
                switch (write)
                {
                    case InvokeWrite.Written written:
                        return await AnswerMoved(
                                row,
                                machine,
                                current,
                                invokeToken,
                                end,
                                outcome,
                                reason,
                                planned,
                                written,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    case InvokeWrite.Refused refused:
                        LogRefused(row, planned, refused);
                        problem = InvokeOutcomeReasons.NextRunRefused;
                        break;
                    default:
                        return null;
                }
            }

            // The run's own outcome could not be applied. A Done becomes the state's failure, which says where a
            // finished run that produced nothing usable goes; a failure that cannot be applied either ends with
            // the token cleared, so nothing waits on a run that has finished.
            if (outcome.Kind == InvokeOutcomeKind.Done)
            {
                outcome = Fail(problem.NotNull(), out reason);
                continue;
            }

            return await Strand(row, current, invokeToken, problem.NotNull(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // The outcome the run's end applies, and the reason it is applied as a failure when it ended otherwise.
    private static InvokeOutcome OutcomeOf(InvokedRunEnd end, out string? reason)
    {
        reason = end.Reason;
        return end.Kind switch
        {
            InvokeOutcomeKind.Failed => new InvokeOutcome.Failed(),
            InvokeOutcomeKind.Cancelled => new InvokeOutcome.Cancelled(),
            _ when end.Oversize => Fail(InvokeOutcomeReasons.TooLarge, out reason),
            _ when end.Output is null => Fail(InvokeOutcomeReasons.OutputUnrecorded, out reason),
            _ => DoneWith(end.Output, out reason),
        };
    }

    private async Task<InvokeDelivery> AnswerMoved(
        StoredInstance row,
        IMachineInternals machine,
        Snapshot current,
        string invokeToken,
        InvokedRunEnd end,
        InvokeOutcome outcome,
        string? reason,
        Snapshot planned,
        InvokeWrite.Written written,
        CancellationToken cancellationToken
    )
    {
        var moved = new InvokeDelivery.Moved(
            row.Machine,
            row.Id,
            current.State,
            planned.State,
            OutcomeTriggers.Suffix(outcome.Kind),
            reason,
            written.InvokeToken
        );
        if (reason is not null)
            logger.LogWarning(
                "The run {RunId} that {Machine} instance {InstanceId} invoked in {State} "
                    + "ended {Ended}, applied as its failure ({Reason}): the instance is now "
                    + "in {Target}.",
                invokeToken,
                row.Machine,
                row.Id,
                current.State,
                end.Kind,
                reason,
                planned.State
            );
        // The outcome may have ended this instance, when another machine spawned it.
        if (machine.Ends.ContainsKey(planned.State))
            await DeliverEndOf(row.Owner, row.Machine, row.Id, cancellationToken)
                .ConfigureAwait(false);
        return moved;
    }

    private void LogRefused(StoredInstance row, Snapshot planned, InvokeWrite.Refused refused)
    {
        if (refused.Exception is not null)
            logger.LogError(
                refused.Exception,
                "The run {Machine} instance {InstanceId} would start in {Target} could not be "
                    + "queued ({Code}).",
                row.Machine,
                row.Id,
                planned.State,
                refused.Code.ToWire()
            );
    }

    private static InvokeOutcome Fail(string why, out string? reason)
    {
        reason = why;
        return new InvokeOutcome.Failed();
    }

    private static InvokeOutcome DoneWith(string output, out string? reason)
    {
        reason = null;
        try
        {
            return new InvokeOutcome.Done(JsonNode.Parse(output));
        }
        catch (JsonException)
        {
            return Fail(InvokeOutcomeReasons.OutputUnrecorded, out reason);
        }
    }

    // The successor the outcome produces, checked as an advance's is before anything is written; null with the
    // reason when it cannot be stored.
    private Snapshot? Plan(
        IMachineInternals machine,
        Snapshot current,
        InvokeOutcome outcome,
        StoredInstance row,
        out string? problem
    )
    {
        problem = null;
        switch (machine.ApplyOutcome(current, outcome))
        {
            case AdvanceResult.Transitioned transitioned:
                var next = transitioned.Snapshot;
                if (StorableJson.Problem(next.Context) is not null)
                {
                    problem = InvokeOutcomeReasons.OutcomeRejected;
                    return null;
                }
                if (
                    Encoding.UTF8.GetByteCount(machine.Serialize(next))
                    > SnapshotLimits.MaxSnapshotBytes
                )
                {
                    problem = InvokeOutcomeReasons.TooLarge;
                    return null;
                }
                return next;
            case AdvanceResult.Rejected rejected:
                problem =
                    outcome is InvokeOutcome.Done && rejected.Reason == RefusalCode.NoTransition
                        ? InvokeOutcomeReasons.OutputUnaccepted
                        : InvokeOutcomeReasons.OutcomeRejected;
                if (
                    rejected.Exception is not null
                    || string.Equals(
                        problem,
                        InvokeOutcomeReasons.OutcomeRejected,
                        StringComparison.Ordinal
                    )
                )
                    logger.LogError(
                        rejected.Exception,
                        "The {Outcome} outcome of {Machine} instance {InstanceId} in {State} was refused "
                            + "({Reason}).",
                        outcome.Kind,
                        row.Machine,
                        row.Id,
                        current.State,
                        rejected.Reason.ToWire()
                    );
                return null;
            default:
                problem = InvokeOutcomeReasons.OutcomeRejected;
                return null;
        }
    }

    private async Task<InvokeDelivery?> Strand(
        StoredInstance row,
        Snapshot current,
        string invokeToken,
        string reason,
        CancellationToken cancellationToken
    )
    {
        // One conditional update that clears the token and marks the state stranded, so the instance in an
        // invoking state with no run is recorded as deliberate. No run is queued, so the outbox is not needed.
        if (
            !await store
                .StrandByInvokeToken(invokeToken, current, row.Snapshot.Token, cancellationToken)
                .ConfigureAwait(false)
        )
            return null;

        logger.LogError(
            "The run {RunId} that {Machine} instance {InstanceId} invoked in {State} ended, but no outcome could "
                + "be applied ({Reason}). The instance stays in {State} with no live run, and leaves it only "
                + "through one of its declared transitions.",
            invokeToken,
            row.Machine,
            row.Id,
            current.State,
            reason,
            current.State
        );
        return new InvokeDelivery.Stranded(row.Machine, row.Id, current.State, reason);
    }
}

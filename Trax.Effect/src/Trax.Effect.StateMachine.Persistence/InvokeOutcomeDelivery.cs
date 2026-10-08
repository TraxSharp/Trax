using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The typed reasons an invoked run's outcome is applied as a failure, or not applied at all. Each is logged with
/// the machine, the instance and the state, never with the run's output or the instance's context.
/// </summary>
internal static class InvokeOutcomeReasons
{
    /// <summary>The run's output, or the snapshot its reduction produced, is past the 64 KiB snapshot cap.</summary>
    public const string TooLarge = "invoke-outcome-too-large";

    /// <summary>The run completed, but no <c>OnDone</c> edge's guard accepts its output.</summary>
    public const string OutputUnaccepted = "invoke-output-unaccepted";

    /// <summary>The run completed, but its output could not be recorded for the machine (it could not be serialized).</summary>
    public const string OutputUnrecorded = "invoke-output-unrecorded";

    /// <summary>The chosen edge's reduction produced a context its target refuses, or threw.</summary>
    public const string OutcomeRejected = "invoke-outcome-rejected";

    /// <summary>The target invokes a train of its own, and that run could not be queued.</summary>
    public const string NextRunRefused = "invoke-next-run-refused";

    /// <summary>
    /// The run can no longer be found: neither its work queue entry nor its metadata row exists, so something
    /// (metadata retention, an operator) deleted them before its outcome was delivered. How it ended is unknown.
    /// </summary>
    public const string RunMissing = "invoke-run-missing";
}

/// <summary>How an invoked run ended, as the machine that invoked it reads it.</summary>
/// <param name="Kind">Done, Failed or Cancelled.</param>
/// <param name="Output">A completed run's recorded output, or null.</param>
/// <param name="Oversize">True when a completed run's output was too large to record.</param>
/// <param name="Reason">Why the end is applied as a failure the run did not report itself, one of <see cref="InvokeOutcomeReasons"/>; null otherwise.</param>
internal sealed record InvokedRunEnd(
    InvokeOutcomeKind Kind,
    string? Output,
    bool Oversize,
    string? Reason = null
);

/// <summary>What one delivery of an invoked run's outcome did.</summary>
internal abstract record InvokeDelivery
{
    /// <summary>The outcome moved the instance: the conditional update matched, once.</summary>
    /// <param name="Machine">The machine.</param>
    /// <param name="Id">The instance.</param>
    /// <param name="From">The invoking state the run belonged to.</param>
    /// <param name="To">The state the outcome entered.</param>
    /// <param name="Applied">The outcome applied, <c>done</c>, <c>failed</c> or <c>cancelled</c>: the run's own, or <c>failed</c> when it could not be.</param>
    /// <param name="Reason">Why a run's own outcome was applied as Failed, one of <see cref="InvokeOutcomeReasons"/>; null otherwise.</param>
    /// <param name="NextToken">The run the entered state queued, when it invokes a train.</param>
    public sealed record Moved(
        string Machine,
        Guid Id,
        string From,
        string To,
        string Applied,
        string? Reason,
        string? NextToken
    ) : InvokeDelivery;

    /// <summary>
    /// No row holds the run's token: the outcome was applied already, or the state it belonged to was left (by a
    /// declared transition, which cancelled the run). Nothing is written. A typed no-transition.
    /// </summary>
    public sealed record NoTransition : InvokeDelivery;

    /// <summary>The run has not ended. Nothing is written.</summary>
    public sealed record Running : InvokeDelivery;

    /// <summary>The row belongs to a machine this host does not register, or cannot read. Another host applies it.</summary>
    /// <param name="Machine">The row's machine.</param>
    public sealed record NotHere(string Machine) : InvokeDelivery;

    /// <summary>The row kept changing under every attempt; the next sweep tries again.</summary>
    public sealed record Contended : InvokeDelivery;

    /// <summary>
    /// Not even the run's failure could be applied (the <c>OnFailed</c> or <c>OnCancelled</c> edge's reduction
    /// is refused, or its target's run cannot be queued). The token is cleared, so the instance stays in the state
    /// with no live run and leaves it only through one of its declared transitions; the reason is logged.
    /// </summary>
    /// <param name="Machine">The machine.</param>
    /// <param name="Id">The instance.</param>
    /// <param name="State">The state it stays in.</param>
    /// <param name="Reason">One of <see cref="InvokeOutcomeReasons"/>.</param>
    public sealed record Stranded(string Machine, Guid Id, string State, string Reason)
        : InvokeDelivery;

    private InvokeDelivery() { }
}

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
[Experimental(ExperimentalIds.Invokes)]
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

    private static readonly TrainState[] Ended =
    [
        TrainState.Completed,
        TrainState.Failed,
        TrainState.Cancelled,
    ];

    private readonly Dictionary<string, IMachine> _machines = machines.ToDictionary(
        m => m.Name,
        StringComparer.Ordinal
    );

    /// <summary>Delivers the outcome of the run whose external id is <paramref name="invokeToken"/>.</summary>
    public async Task<InvokeDelivery> Deliver(
        string invokeToken,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(invokeToken);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var row = await store.GetByInvokeToken(invokeToken, cancellationToken);
            if (row is null)
                return new InvokeDelivery.NoTransition();

            if (
                !_machines.TryGetValue(row.Machine, out var found)
                || found is not IMachineInternals machine
            )
                return new InvokeDelivery.NotHere(row.Machine);

            var end = await ReadEnd(invokeToken, cancellationToken);
            if (end is null)
                return new InvokeDelivery.Running();

            Snapshot current;
            switch (machine.Rehydrate(row.Snapshot.Json))
            {
                case RehydrationResult.Ok ok:
                    current = ok.Snapshot;
                    break;
                case RehydrationResult.Error error:
                    // Another host, with the definition this row was written under, can read it.
                    logger.LogWarning(
                        error.Exception,
                        "The outcome of run {RunId} cannot be applied to {Machine} instance {InstanceId} on this "
                            + "host, which cannot read the stored snapshot ({Code}).",
                        invokeToken,
                        row.Machine,
                        row.Id,
                        error.Code
                    );
                    return new InvokeDelivery.NotHere(row.Machine);
                default:
                    return new InvokeDelivery.NotHere(row.Machine);
            }

            var result = await Apply(row, machine, current, invokeToken, end, cancellationToken);
            if (result is not null)
                return result;
        }

        return new InvokeDelivery.Contended();
    }

    /// <summary>
    /// The tokens among <paramref name="invokeTokens"/> whose runs have ended: the reconciler's sweep reads a page
    /// of live tokens with this and delivers only these. Two queries, whatever the page's size.
    /// </summary>
    public async Task<IReadOnlyList<string>> EndedAmong(
        IReadOnlyCollection<string> invokeTokens,
        CancellationToken cancellationToken = default
    )
    {
        if (invokeTokens.Count == 0)
            return [];

        var entries = await context
            .WorkQueues.AsNoTracking()
            .Where(w => invokeTokens.Contains(w.ExternalId))
            .Select(w => new
            {
                w.ExternalId,
                w.Status,
                w.MetadataId,
            })
            .ToListAsync(cancellationToken);

        var ended = entries
            .Where(e => e.Status == WorkQueueStatus.Cancelled)
            .Select(e => e.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        var runIds = entries
            .Where(e => e.Status == WorkQueueStatus.Dispatched && e.MetadataId is not null)
            .Select(e => e.MetadataId!.Value)
            .ToList();

        // An entry the queue no longer holds leaves the run's own row to say how it ended.
        var unqueued = invokeTokens
            .Except(entries.Select(e => e.ExternalId), StringComparer.Ordinal)
            .ToList();

        if (runIds.Count > 0 || unqueued.Count > 0)
        {
            var runs = await context
                .Metadatas.AsNoTracking()
                .Where(m => runIds.Contains(m.Id) || unqueued.Contains(m.ExternalId))
                .Select(m => new { m.ExternalId, m.TrainState })
                .ToListAsync(cancellationToken);
            foreach (var run in runs.Where(r => Ended.Contains(r.TrainState)))
                ended.Add(run.ExternalId.Trim());

            // A token whose run can no longer be found at all has ended as far as its machine can tell; the
            // delivery reads it again and applies it as a failure (see ReadEnd).
            var found = runs.Select(r => r.ExternalId.Trim()).ToHashSet(StringComparer.Ordinal);
            foreach (var token in unqueued.Where(t => !found.Contains(t)))
                ended.Add(token);
        }

        return invokeTokens.Where(ended.Contains).ToList();
    }

    /// <summary>How the run <paramref name="invokeToken"/> names ended, or null while it has not.</summary>
    internal async Task<InvokedRunEnd?> ReadEnd(
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        var entry = await context
            .WorkQueues.AsNoTracking()
            .Where(w => w.ExternalId == invokeToken)
            .OrderByDescending(w => w.Id)
            .Select(w => new { w.Status, w.MetadataId })
            .FirstOrDefaultAsync(cancellationToken);

        long? runId;
        if (entry is null)
        {
            runId = await context
                .Metadatas.AsNoTracking()
                .Where(m => m.ExternalId == invokeToken)
                .OrderByDescending(m => m.Id)
                .Select(m => (long?)m.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // Neither the entry nor the run exists. That is never a run still to come: the outbox writes the
            // entry in the same transaction that sets the token, and every caller reads the token (the row that
            // holds it) before this, so an entry that was ever written is visible here unless it was deleted.
            // Dispatch only updates an entry, and no cleanup deletes an invoked run's entry on its own: it has no
            // manifest (the manifest pruner), is never staged (the stranded-entry sweep) and never dead-lettered
            // (dead letter cleanup). Metadata retention deletes a finished run together with its entry, and keeps
            // an invoked run while an instance still holds its token. So this is a run whose records were deleted
            // some other way; it can never end, so the machine treats it as failed.
            if (runId is null)
                return new InvokedRunEnd(
                    InvokeOutcomeKind.Failed,
                    null,
                    false,
                    InvokeOutcomeReasons.RunMissing
                );
        }
        else
            switch (entry.Status)
            {
                // Never dispatched: cancelled while queued, by leaving the state, an operator, or a draft's expiry.
                case WorkQueueStatus.Cancelled:
                    return new InvokedRunEnd(InvokeOutcomeKind.Cancelled, null, false);
                // Not yet dispatched, or requeued after a failed dispatch: the run is still to come.
                case WorkQueueStatus.Queued:
                    return null;
                default:
                    runId = entry.MetadataId;
                    break;
            }

        if (runId is null)
            return null;

        var run = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == runId.Value)
            .Select(m => new
            {
                m.TrainState,
                m.InvokeOutput,
                m.InvokeOutputOversize,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return run?.TrainState switch
        {
            TrainState.Completed => new InvokedRunEnd(
                InvokeOutcomeKind.Done,
                run.InvokeOutput,
                run.InvokeOutputOversize
            ),
            TrainState.Failed => new InvokedRunEnd(InvokeOutcomeKind.Failed, null, false),
            TrainState.Cancelled => new InvokedRunEnd(InvokeOutcomeKind.Cancelled, null, false),
            _ => null,
        };
    }

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
        var reason = end.Reason;
        InvokeOutcome outcome = end.Kind switch
        {
            InvokeOutcomeKind.Failed => new InvokeOutcome.Failed(),
            InvokeOutcomeKind.Cancelled => new InvokeOutcome.Cancelled(),
            _ when end.Oversize => Fail(InvokeOutcomeReasons.TooLarge, out reason),
            _ when end.Output is null => Fail(InvokeOutcomeReasons.OutputUnrecorded, out reason),
            _ => DoneWith(end.Output, out reason),
        };

        while (true)
        {
            var planned = Plan(machine, current, outcome, row, out var problem);
            if (planned is not null)
            {
                var entering = machine.Entering(planned.State);
                var write = await outbox.Deliver(
                    row.Owner,
                    row.Id,
                    invokeToken,
                    planned,
                    row.Snapshot.Token,
                    entering,
                    cancellationToken
                );
                switch (write)
                {
                    case InvokeWrite.Written written:
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
                        return moved;
                    case InvokeWrite.Refused refused:
                        if (refused.Exception is not null)
                            logger.LogError(
                                refused.Exception,
                                "The run {Machine} instance {InstanceId} would start in {Target} could not be "
                                    + "queued ({Code}).",
                                row.Machine,
                                row.Id,
                                planned.State,
                                refused.Code
                            );
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
                outcome = Fail(problem!, out reason);
                continue;
            }

            return await Strand(row, current, invokeToken, problem!, cancellationToken);
        }
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
                    outcome is InvokeOutcome.Done
                    && rejected.Reason == RejectionReasons.NoTransition
                        ? InvokeOutcomeReasons.OutputUnaccepted
                        : InvokeOutcomeReasons.OutcomeRejected;
                if (
                    rejected.Exception is not null
                    || problem == InvokeOutcomeReasons.OutcomeRejected
                )
                    logger.LogError(
                        rejected.Exception,
                        "The {Outcome} outcome of {Machine} instance {InstanceId} in {State} was refused "
                            + "({Reason}).",
                        outcome.Kind,
                        row.Machine,
                        row.Id,
                        current.State,
                        rejected.Reason
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
        var write = await outbox.Deliver(
            row.Owner,
            row.Id,
            invokeToken,
            current,
            row.Snapshot.Token,
            entering: null,
            cancellationToken
        );
        if (write is not InvokeWrite.Written)
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

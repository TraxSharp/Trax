using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Cancels an invoked run by its invoke token, the run's external id, the way the operations surface cancels: a
/// work queue entry still <c>Queued</c> is marked <c>Cancelled</c>, so it is never dispatched, and a run already
/// dispatched (<c>Pending</c> or <c>InProgress</c>) has its <c>cancellation_requested</c> flag set, which the run
/// reads at its next junction boundary on whichever host executes it. A run already finished is left alone.
/// </summary>
/// <remarks>
/// <para>Both writes are conditional single statements, so a cancel racing the dispatcher's claim ends one way: the
/// claim and the run's metadata commit in one transaction, so either the entry is still queued and is cancelled, or
/// the run exists and is flagged.</para>
/// <para>The flag is the cross-host request. Same-host immediate cancellation through the scheduler's cancellation
/// registry is not reachable from here; the run stops at its next junction instead.</para>
/// </remarks>
[Experimental(ExperimentalIds.Invokes)]
internal sealed class InvokedRunCancellation(IDataContext context) : IInvokedRunCancellation
{
    /// <inheritdoc/>
    public Task Cancel(string invokeToken, CancellationToken cancellationToken = default) =>
        CancelIn(context, invokeToken, cancellationToken);

    /// <summary>
    /// Cancels the run through <paramref name="context"/>, inside whatever transaction it holds, so leaving an
    /// invoking state cancels its run in the write that leaves it.
    /// </summary>
    /// <returns>True when a queued entry was cancelled or a dispatched run was flagged.</returns>
    internal static async Task<bool> CancelIn(
        IDataContext context,
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(invokeToken);

        var queued = context.WorkQueues.Where(w =>
            w.ExternalId == invokeToken && w.Status == WorkQueueStatus.Queued
        );
        var running = context.Metadatas.Where(m =>
            m.ExternalId == invokeToken
            && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
            && !m.CancellationRequested
        );

        if (context is DbContext db && db.Database.IsRelational())
        {
            var cancelled = await queued.ExecuteUpdateAsync(
                s => s.SetProperty(w => w.Status, WorkQueueStatus.Cancelled),
                cancellationToken
            );
            if (cancelled > 0)
                return true;

            return await running.ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.CancellationRequested, true),
                    cancellationToken
                ) > 0;
        }

        // A provider without set updates (InMemory) loads the rows and saves them.
        var entries = await queued.ToListAsync(cancellationToken);
        foreach (var entry in entries)
            entry.Status = WorkQueueStatus.Cancelled;
        var runs = entries.Count > 0 ? [] : await running.ToListAsync(cancellationToken);
        foreach (var run in runs)
            run.CancellationRequested = true;
        await context.SaveChanges(cancellationToken);
        return entries.Count + runs.Count > 0;
    }
}

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;

namespace Trax.Effect.Data.Services.WorkQueuePromotion;

/// <inheritdoc />
public class WorkQueuePromotion(IDataContextProviderFactory contextFactory) : IWorkQueuePromotion
{
    /// <inheritdoc />
    public async Task<bool> PromoteAsync(long workQueueId, CancellationToken cancellationToken)
    {
        using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var promoted = await UpdateAsync(
            context,
            w => w.Id == workQueueId && w.ConfirmedAt == null && w.Status == WorkQueueStatus.Queued,
            confirm: true,
            cancellationToken
        );

        return promoted > 0;
    }

    /// <summary>
    /// How long a staged entry the sweep cancelled is kept, counted from when it was created.
    /// </summary>
    /// <remarks>
    /// The same as the scheduler's default dead-letter retention, the other record Trax keeps
    /// for an operator to act on. A cancelled staged entry is the only record of which enqueue
    /// vanished; the sweep logs a count, not the entries. Rows accrue only when a host dies
    /// mid-enqueue, so keeping them this long costs next to nothing. See
    /// docs/adr/0007-cancelled-staged-entries-are-deleted-after-a-retention.md.
    /// </remarks>
    internal static readonly TimeSpan CancelledStagedEntryRetention = TimeSpan.FromDays(30);

    /// <inheritdoc />
    public async Task<int> CancelStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Deleted before this pass cancels anything, so an entry is only ever deleted by a pass
        // after the one that cancelled it, however late the sweep runs.
        await DeleteAsync(
            context,
            CancelledPastRetention(olderThan + CancelledStagedEntryRetention),
            cancellationToken
        );

        return await UpdateAsync(context, Stale(olderThan), confirm: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> PromoteStaleAsync(
        TimeSpan olderThan,
        CancellationToken cancellationToken
    )
    {
        using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await UpdateAsync(context, Stale(olderThan), confirm: true, cancellationToken);
    }

    private static Expression<Func<WorkQueue, bool>> Stale(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;

        return w =>
            w.ConfirmedAt == null && w.Status == WorkQueueStatus.Queued && w.CreatedAt < cutoff;
    }

    /// <summary>
    /// Staged entries that were cancelled before they were confirmed and are older than
    /// <paramref name="olderThan"/>. That is what the sweep cancels, and also an entry an
    /// operator cancelled while its hook ran; either way it never ran and has no metadata.
    /// </summary>
    private static Expression<Func<WorkQueue, bool>> CancelledPastRetention(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;

        return w =>
            w.ConfirmedAt == null
            && w.Status == WorkQueueStatus.Cancelled
            && w.MetadataId == null
            && w.CreatedAt < cutoff;
    }

    /// <summary>
    /// Deletes the matching entries in one statement where the provider can, and through change
    /// tracking where it cannot, for the reason <see cref="UpdateAsync"/> gives.
    /// </summary>
    private static async Task<int> DeleteAsync(
        IDataContext context,
        Expression<Func<WorkQueue, bool>> match,
        CancellationToken cancellationToken
    )
    {
        if (context is DbContext db && db.Database.IsRelational())
            return await context.WorkQueues.Where(match).ExecuteDeleteAsync(cancellationToken);

        var entries = await context.WorkQueues.Where(match).ToListAsync(cancellationToken);

        context.WorkQueues.RemoveRange(entries);
        await context.SaveChanges(cancellationToken);

        return entries.Count;
    }

    /// <summary>
    /// Confirms or cancels the matching entries in one statement where the provider can, and
    /// through change tracking where it cannot.
    /// </summary>
    /// <remarks>
    /// The in-memory provider does not translate <c>ExecuteUpdate</c>. Falling back keeps a
    /// deferring train usable in tests and local development rather than throwing after its hook
    /// has already run.
    /// </remarks>
    private static async Task<int> UpdateAsync(
        IDataContext context,
        Expression<Func<WorkQueue, bool>> match,
        bool confirm,
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;

        if (context is DbContext db && db.Database.IsRelational())
            return confirm
                ? await context
                    .WorkQueues.Where(match)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(w => w.ConfirmedAt, now),
                        cancellationToken
                    )
                : await context
                    .WorkQueues.Where(match)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(w => w.Status, WorkQueueStatus.Cancelled),
                        cancellationToken
                    );

        var entries = await context.WorkQueues.Where(match).ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            if (confirm)
                entry.ConfirmedAt = now;
            else
                entry.Status = WorkQueueStatus.Cancelled;
        }

        await context.SaveChanges(cancellationToken);

        return entries.Count;
    }
}

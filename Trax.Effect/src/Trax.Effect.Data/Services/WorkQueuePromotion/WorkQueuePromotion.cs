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

    /// <inheritdoc />
    public async Task<int> CancelStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

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

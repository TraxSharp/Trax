using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Trax.Effect.Data.Services.QueuedWorkListener;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;

namespace Trax.Effect.Data.Sqlite.Services.QueuedWorkListener;

/// <summary>
/// The SQLite <see cref="IQueuedWorkListener"/>: an in-process notice, raised once a save through a
/// data context of this provider commits a work queue entry that can be dispatched.
/// </summary>
/// <remarks>
/// <para>SQLite has no notification channel, so only a dispatcher in the same process (the same
/// service provider) hears it. That is the usual SQLite deployment; any other writer is picked up
/// by the dispatcher's poll, as is a staged entry confirmed with a set-based update, which does
/// not pass through <c>SaveChanges</c>.</para>
///
/// <para>A save marks its context when it holds an entry that is added as queued and confirmed, or
/// whose <c>confirmed_at</c> it sets. The notice goes out when the transaction the save wrote in
/// commits: at once for a save outside a transaction, at <c>CommitTransaction</c> for one inside,
/// and never if that transaction rolls back.</para>
/// </remarks>
internal sealed class SqliteQueuedWorkSignal
    : IQueuedWorkListener,
        ISaveChangesInterceptor,
        IDbTransactionInterceptor
{
    private static readonly object Marked = new();

    private readonly ConditionalWeakTable<DbContext, object> _marked = new();
    private readonly Lock _gate = new();
    private readonly List<Subscription> _subscriptions = [];

    public Task<IQueuedWorkSubscription> SubscribeAsync(CancellationToken cancellationToken)
    {
        var subscription = new Subscription(this);
        lock (_gate)
            _subscriptions.Add(subscription);
        return Task.FromResult<IQueuedWorkSubscription>(subscription);
    }

    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Mark(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Mark(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSave(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        AfterSave(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) =>
        AfterFailedSave(eventData.Context);

    public Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        AfterFailedSave(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData
    ) => Release(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Release(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is { } context)
            _marked.Remove(context);
    }

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        TransactionRolledBack(transaction, eventData);
        return Task.CompletedTask;
    }

    private void Mark(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<WorkQueue>())
        {
            if (IsDispatchable(entry))
            {
                _marked.AddOrUpdate(context, Marked);
                return;
            }
        }
    }

    private static bool IsDispatchable(EntityEntry<WorkQueue> entry)
    {
        if (entry.Entity.Status != WorkQueueStatus.Queued || entry.Entity.ConfirmedAt is null)
            return false;

        if (entry.State == EntityState.Added)
            return true;

        var confirmedAt = entry.Property(w => w.ConfirmedAt);
        return entry.State == EntityState.Modified
            && confirmedAt.IsModified
            && confirmedAt.OriginalValue is null;
    }

    // A save outside a transaction has committed by now. One inside a transaction (the caller's,
    // or the one EF opened for a multi-statement save) is released when that commits.
    private void AfterSave(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null)
            Release(context);
    }

    private void AfterFailedSave(DbContext? context)
    {
        if (context is not null && context.Database.CurrentTransaction is null)
            _marked.Remove(context);
    }

    private void Release(DbContext? context)
    {
        if (context is null || !_marked.Remove(context))
            return;

        lock (_gate)
        {
            foreach (var subscription in _subscriptions)
                subscription.Notify();
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
            _subscriptions.Remove(subscription);
    }

    private sealed class Subscription(SqliteQueuedWorkSignal owner) : IQueuedWorkSubscription
    {
        private readonly SemaphoreSlim _notices = new(0);

        public void Notify() => _notices.Release();

        public Task WaitAsync(CancellationToken cancellationToken) =>
            _notices.WaitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            owner.Unsubscribe(this);
            _notices.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

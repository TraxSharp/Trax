namespace Trax.Effect.Data.Services.QueuedWorkListener;

/// <summary>
/// A data provider's notice that work queue entries became dispatchable, so a dispatcher can start
/// a cycle at once instead of waiting for its next poll. The poll stays the fallback: a notice can
/// be lost while a subscription reconnects, and a provider is free to register none.
/// </summary>
/// <remarks>
/// <para><c>UsePostgres</c> registers one that hears every host: a trigger on <c>work_queue</c> sends a
/// <c>NOTIFY</c> in the transaction that inserts an entry or confirms a staged one, so it is
/// delivered when that transaction commits and never when it rolls back. <c>UseSqlite</c> registers
/// one that hears this process only, raised after a save through its data context commits a
/// dispatchable entry. The InMemory provider registers none.</para>
///
/// <para>A notice says only that something may be ready. It carries no entry, and a receiver
/// that then loads nothing is behaving correctly.</para>
/// </remarks>
public interface IQueuedWorkListener
{
    /// <summary>
    /// Starts listening. The returned subscription hears every notice sent after this completes,
    /// until it is disposed.
    /// </summary>
    Task<IQueuedWorkSubscription> SubscribeAsync(CancellationToken cancellationToken);
}

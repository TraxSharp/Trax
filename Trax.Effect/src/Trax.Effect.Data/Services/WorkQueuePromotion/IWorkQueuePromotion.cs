namespace Trax.Effect.Data.Services.WorkQueuePromotion;

/// <summary>
/// Confirms deferred work queue entries, the second phase of a two-phase enqueue, and resolves
/// the ones a crash left unconfirmed.
/// </summary>
/// <remarks>
/// An entry created with <c>CreateWorkQueue.DeferPromotion</c> is committed unconfirmed and is not
/// dispatchable. The enqueue promotes it once its <c>OnQueue</c> hook has returned, and removes it
/// if the hook threw. If the process dies in between, the entry is left unconfirmed rather than
/// the hook's side-effect being left with nothing to consume it. Such an entry is resolved by
/// <see cref="CancelStaleAsync"/>, which the scheduler runs by default, or by
/// <see cref="PromoteStaleAsync"/> for a host that opts into promotion.
/// </remarks>
public interface IWorkQueuePromotion
{
    /// <summary>
    /// Marks one queued entry dispatchable. Returns false when the entry does not exist, was
    /// already promoted, or is no longer queued (it was cancelled while its hook ran).
    /// </summary>
    Task<bool> PromoteAsync(long workQueueId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels every queued entry left unconfirmed for longer than <paramref name="olderThan"/>
    /// and returns how many were cancelled.
    /// </summary>
    /// <remarks>
    /// The safe default, because a stranded entry is ambiguous. The process may have died after
    /// the hook succeeded, before it ran, or after it threw and before the entry was removed.
    /// Nothing recorded tells these apart, and only the first is a mutation that was accepted.
    /// Cancelling keeps the entry visible, so the side-effect a hook may have left can be found
    /// and reconciled.
    /// </remarks>
    Task<int> CancelStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken);

    /// <summary>
    /// Promotes every queued entry left unconfirmed for longer than <paramref name="olderThan"/>
    /// and returns how many were promoted.
    /// </summary>
    /// <remarks>
    /// For a host whose deferring trains can safely run a mutation whose hook may never have run,
    /// or may have rejected it. The run re-executes the whole <c>Junctions()</c> chain, so a hook
    /// side-effect that did land is re-applied and one that did not is performed for the first
    /// time, which assumes the hook is idempotent and that rejection is re-checked by the chain.
    /// </remarks>
    Task<int> PromoteStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken);
}

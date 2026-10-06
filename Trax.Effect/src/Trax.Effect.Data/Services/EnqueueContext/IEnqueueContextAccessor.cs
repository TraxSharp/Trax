using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Services.EnqueueContext;

/// <summary>
/// Exposes the data context that the enqueue path is currently committing on, so a
/// <c>ServiceTrain.OnQueue</c> hook can make its side-effect part of the same transaction as the
/// work queue row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Current"/> is non-null only while <c>OnQueue</c> is running for a train that does
/// not defer promotion. Everywhere else it is null, and a consumer must fall back to its own
/// context. That includes a train with <c>DeferQueuePromotion</c> set: its entry is committed
/// before the hook runs, so there is no open enqueue transaction for the hook to join, and
/// deferral exists for hooks that write elsewhere.
/// </para>
/// <para>
/// The value flows with the async call, so concurrent enqueues on one scope each see their own.
/// An enqueue started from inside a hook joins the enqueue it runs inside: Trax.Mediator writes
/// its entry on the outer enqueue's context and transaction, so it commits or rolls back with the
/// outer one (Trax.Mediator docs/adr/0003). Only where there is no open outer transaction to join,
/// such as inside a deferring train's hook, does it commit on a context of its own.
/// </para>
/// <para>
/// Writes tracked on <see cref="Current"/> are saved and committed by the enqueue, so a hook that
/// throws — or a queue row that fails to insert — rolls the hook's work back with it. A hook that
/// writes through its own <c>DbContext</c> does NOT get that guarantee: EF can only share a
/// transaction between contexts that share a connection, and a separately-pooled context does not.
/// </para>
/// <para>
/// The hook must not call <c>SaveChanges</c> or commit on <see cref="Current"/>; the enqueue owns
/// the lifetime.
/// </para>
/// </remarks>
public interface IEnqueueContextAccessor
{
    /// <summary>
    /// The context the enqueue is committing on, or null when no enqueue is in progress.
    /// </summary>
    IDataContext? Current { get; }

    /// <summary>
    /// Makes <paramref name="context"/> the ambient enqueue context for the current async flow until
    /// the returned scope is disposed, which restores whatever was current before. Called by the
    /// framework's enqueue path; consumers read <see cref="Current"/>.
    /// </summary>
    IDisposable Enter(IDataContext context);

    /// <summary>
    /// Makes <see cref="Current"/> null for the current async flow until the returned scope is
    /// disposed, which restores whatever was current before. The enqueue path calls it around the
    /// <c>OnQueue</c> hook of a train that defers promotion: that hook has no enqueue transaction to
    /// join, even when the enqueue that runs it was started from inside another train's hook.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Enter"/>, it affects only the async flow that calls it and the work that flow
    /// goes on to start. A concurrent enqueue on the same scope keeps its own context.
    /// </remarks>
    IDisposable Suppress();
}

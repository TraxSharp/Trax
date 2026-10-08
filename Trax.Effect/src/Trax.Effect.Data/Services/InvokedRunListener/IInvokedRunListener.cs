using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Trax.Effect.Data.Services.InvokedRunListener;

/// <summary>
/// A data provider's notice that a run a state machine's invoking state queued has ended, so the state machine's
/// outcome reconciler can apply its outcome at once instead of at its next sweep. The sweep stays the guarantee: a
/// notice can be lost while a subscription reconnects, and a provider is free to register none.
/// </summary>
/// <remarks>
/// <para><c>UsePostgres</c> registers one that hears every host: triggers on <c>metadata</c> and <c>work_queue</c>
/// send a <c>NOTIFY</c> carrying the run's external id when an invoked run's row becomes completed, failed or
/// cancelled, or its still-queued entry is cancelled, delivered when that transaction commits. SQLite and InMemory
/// register none: on them the run's own host applies the outcome through its lifecycle hook, and the sweep covers
/// the rest.</para>
///
/// <para>Infrastructure for <c>Trax.Effect.StateMachine.Persistence</c>; a host does not implement or call it.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IInvokedRunListener
{
    /// <summary>
    /// Starts listening. The returned subscription hears every notice sent after this completes, until it is
    /// disposed.
    /// </summary>
    Task<IInvokedRunSubscription> SubscribeAsync(CancellationToken cancellationToken);
}

/// <summary>One listening session opened by <see cref="IInvokedRunListener.SubscribeAsync"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IInvokedRunSubscription : IAsyncDisposable
{
    /// <summary>
    /// Completes with the external id of the next invoked run that ended. Throws when the session is lost, for
    /// example when its database connection is closed; dispose it and subscribe again.
    /// </summary>
    Task<string> NextAsync(CancellationToken cancellationToken);
}

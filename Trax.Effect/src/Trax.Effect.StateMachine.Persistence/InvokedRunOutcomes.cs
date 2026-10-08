using System.Diagnostics.CodeAnalysis;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Applies the outcome of an invoked run to the instance waiting on it, now, rather than leaving it to the next
/// sweep. It is the delivery the lifecycle hook and the reconciler make: one conditional update on the instance's
/// invoke token, so however many callers deliver one run, from however many hosts, the outcome is applied once.
/// <c>AddStateMachines</c> registers it; a host without machines has none, and there the reconciler on a host that
/// registers the machine applies the outcome instead.
/// </summary>
/// <remarks>
/// The operations service calls it after an operator cancels a system-owned instance's still-queued run, so the
/// instance moves through its <c>OnCancelled</c> edge in the operator's request when this host can read the
/// machine. See <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </remarks>
[Experimental(ExperimentalIds.Invokes)]
public interface IInvokedRunOutcomes
{
    /// <summary>
    /// Applies the outcome of the run whose invoke token is <paramref name="invokeToken"/>, when the run has ended
    /// and an instance still holds the token.
    /// </summary>
    /// <param name="invokeToken">The run's external id, the token the waiting instance holds.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <returns>
    /// The state this call moved the instance into, or <c>null</c> when it moved nothing: the run has not ended, no
    /// instance holds the token (the outcome was applied already, by this call's race or by the hook or the
    /// reconciler), this host does not register or cannot read the machine, or the delivery failed and the next
    /// sweep retries it.
    /// </returns>
    Task<string?> Deliver(string invokeToken, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IInvokedRunOutcomes"/>: the reconciler's own delivery of one run.</summary>
[Experimental(ExperimentalIds.Invokes)]
internal sealed class InvokedRunOutcomes(InvokeOutcomeReconciler reconciler) : IInvokedRunOutcomes
{
    public async Task<string?> Deliver(
        string invokeToken,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(invokeToken);
        return
            await reconciler.TryDeliver(invokeToken, cancellationToken)
                is InvokeDelivery.Moved moved
            ? moved.To
            : null;
    }
}

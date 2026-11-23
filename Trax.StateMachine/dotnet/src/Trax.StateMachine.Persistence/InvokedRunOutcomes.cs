using System.Diagnostics.CodeAnalysis;

namespace Trax.StateMachine.Persistence;

/// <summary>The default <see cref="IInvokedRunOutcomes"/>: the reconciler's own delivery of one run.</summary>
internal sealed class InvokedRunOutcomes(InvokeOutcomeReconciler reconciler) : IInvokedRunOutcomes
{
    public async Task<string?> Deliver(
        string invokeToken,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(invokeToken);
        return
            await reconciler.TryDeliver(invokeToken, cancellationToken).ConfigureAwait(false)
                is InvokeDelivery.Moved moved
            ? moved.To
            : null;
    }
}

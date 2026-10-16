using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The fast path of a child's end: after a write that may have put a spawned instance in one of its end states, the
/// writer delivers the end to the parent waiting on it. The reconciler's sweep is the guarantee: a delivery that
/// fails here is logged, and the next sweep makes it. See
/// <c>Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md</c>.
/// </summary>
internal static class SpawnedEnds
{
    /// <summary>
    /// Delivers the end of <paramref name="owner"/>'s instance <paramref name="id"/> of <paramref name="machine"/> to
    /// its parent, in a scope of its own. Never throws, except to pass on a cancellation of
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    internal static async Task Notify(
        IServiceScopeFactory scopes,
        DraftOwner owner,
        string machine,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            await scope
                .ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>()
                .DeliverEndOf(owner, machine, id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            scope
                .ServiceProvider.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(SpawnedEnds))
                .LogWarning(
                    ex,
                    "The end of {Machine} instance {InstanceId} could not be delivered to the instance that spawned "
                        + "it; the reconciler's next sweep delivers it.",
                    machine,
                    id
                );
        }
    }
}

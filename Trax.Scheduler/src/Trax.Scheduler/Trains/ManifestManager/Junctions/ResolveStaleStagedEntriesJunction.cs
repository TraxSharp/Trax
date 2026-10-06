using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Resolves work queue entries a crash left unconfirmed in the middle of a two-phase enqueue.
/// </summary>
/// <remarks>
/// A train with <c>DeferQueuePromotion</c> commits its entry unconfirmed, runs its
/// <c>OnQueue</c> hook, then confirms it. A process that dies in between leaves an entry that is
/// never dispatched. Once one is older than
/// <see cref="SchedulerConfiguration.StaleStagedEntryTimeout"/> it is cancelled, or promoted
/// when the host opted in with <c>PromoteStaleStagedEntries()</c>.
///
/// Cancelling is the default because nothing recorded says whether the hook succeeded, never
/// ran, or rejected the mutation and the entry's removal was what failed. A cancelled entry
/// stays visible, so a side-effect the hook may have left can be found and reconciled.
/// </remarks>
internal class ResolveStaleStagedEntriesJunction(
    IWorkQueuePromotion promotion,
    SchedulerConfiguration config,
    ILogger<ResolveStaleStagedEntriesJunction> logger
) : EffectJunction<List<ManifestDispatchView>, List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(List<ManifestDispatchView> views)
    {
        var resolved = config.PromoteStaleStagedEntries
            ? await promotion.PromoteStaleAsync(config.StaleStagedEntryTimeout, CancellationToken)
            : await promotion.CancelStaleAsync(config.StaleStagedEntryTimeout, CancellationToken);

        if (resolved > 0)
            logger.LogWarning(
                "{Count} work queue entries were left unconfirmed for longer than {Timeout} and were {Resolution}. "
                    + "Each is an enqueue whose process stopped between staging the entry and confirming it.",
                resolved,
                config.StaleStagedEntryTimeout,
                config.PromoteStaleStagedEntries ? "promoted" : "cancelled"
            );

        return views;
    }
}

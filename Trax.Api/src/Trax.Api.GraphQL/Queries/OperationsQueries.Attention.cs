using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

// What needs attention, read through IOperationsService as the console's Overview reads it (central docs/0022), under
// the operations field and so behind the host's operations gate.
public partial class OperationsQueries
{
    /// <summary>
    /// What needs an operator's attention: whether the manifest manager and the job dispatcher are
    /// switched on (<c>scheduler</c>), the dead letters awaiting intervention grouped by train and
    /// reason (<c>deadLetters</c>), the trains whose failure rate in the last hour is above their
    /// rate over the week before it (<c>failingTrains</c>), the stuck state-machine instances by
    /// machine and state (<c>stuckInstances</c>), the queued work that is due and how long the
    /// oldest has waited (<c>queue</c>), and the hosts that have missed three heartbeats
    /// (<c>offlineHosts</c>). Each entry's <c>link</c> is the console path and query that lists
    /// it. Read through <see cref="IOperationsService.GetAttentionAsync"/>.
    /// </summary>
    /// <remarks>
    /// Each list holds at most 50 entries, largest or worst first. Groups name a machine and a
    /// state, never an instance's owner or context.
    /// </remarks>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    public Task<Attention> GetAttention(
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => operationsService.GetAttentionAsync(ct);
}

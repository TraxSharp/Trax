using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

// SIX trains for ALL machines. No per-machine plumbing: the machine is a runtime argument the registry resolves.
// AddStateMachines registers their routes once, so the mediator dispatches them for host code and a machine's own
// surface. They carry no GraphQL attribute: they are served under the `stateMachine` namespace only when the host
// calls ExposeGenericStateMachineMutations, which registers each one's exposure with the policy that gates it. Each
// withholds its output, an instance's snapshot, from the lifecycle hooks: the broadcaster and SignalR never carry it.

/// <summary>Autosave mutation train (<c>stateMachine.saveSnapshot</c>): validates a client snapshot and persists it through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class SaveSnapshot : ServiceTrain<SaveSnapshotInput, SaveSnapshotOutput>, ISaveSnapshot
{
    /// <summary>Chains the single <see cref="SaveSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, SaveSnapshotOutput>> Junctions() =>
        Chain<SaveSnapshotJunction>().Resolve();
}

using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Discard mutation train (<c>stateMachine.discardSnapshot</c>): deletes one of the caller's instances through its machine's <see cref="ISnapshotDraftService"/>, cancelling a run its state invoked first. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class DiscardSnapshot
    : ServiceTrain<DiscardSnapshotInput, DiscardSnapshotOutput>,
        IDiscardSnapshot
{
    /// <summary>Chains the single <see cref="DiscardSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, DiscardSnapshotOutput>> Junctions() =>
        Chain<DiscardSnapshotJunction>().Resolve();
}

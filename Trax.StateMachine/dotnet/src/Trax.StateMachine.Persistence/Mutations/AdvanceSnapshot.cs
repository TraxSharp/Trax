using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Authoritative advance mutation train (<c>stateMachine.advanceSnapshot</c>): re-drives the stored draft by one trigger through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class AdvanceSnapshot
    : ServiceTrain<AdvanceSnapshotInput, AdvanceSnapshotOutput>,
        IAdvanceSnapshot
{
    /// <summary>Chains the single <see cref="AdvanceSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, AdvanceSnapshotOutput>> Junctions() =>
        Chain<AdvanceSnapshotJunction>().Resolve();
}

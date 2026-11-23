using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Resume mutation train (<c>stateMachine.loadSnapshot</c>): reads the caller's stored draft through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class LoadSnapshot : ServiceTrain<LoadSnapshotInput, LoadSnapshotOutput>, ILoadSnapshot
{
    /// <summary>Chains the single <see cref="LoadSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, LoadSnapshotOutput>> Junctions() =>
        Chain<LoadSnapshotJunction>().Resolve();
}

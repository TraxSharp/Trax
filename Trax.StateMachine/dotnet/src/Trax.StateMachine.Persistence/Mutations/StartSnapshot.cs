using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Start mutation train (<c>stateMachine.startSnapshot</c>): creates a new instance from the machine's declared start through its <see cref="ISnapshotDraftService"/>, under an id the server mints. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class StartSnapshot : ServiceTrain<StartSnapshotInput, StartSnapshotOutput>, IStartSnapshot
{
    /// <summary>Chains the single <see cref="StartSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, StartSnapshotOutput>> Junctions() =>
        Chain<StartSnapshotJunction>().Resolve();
}

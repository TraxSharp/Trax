using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Send mutation train (<c>stateMachine.sendSnapshot</c>): runs the machine's one irreversible effect exactly once through its <see cref="ISnapshotEffectRunner"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class SendSnapshot : ServiceTrain<SendSnapshotInput, SendSnapshotOutput>, ISendSnapshot
{
    /// <summary>Chains the single <see cref="SendSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, SendSnapshotOutput>> Junctions() =>
        Chain<SendSnapshotJunction>().Resolve();
}

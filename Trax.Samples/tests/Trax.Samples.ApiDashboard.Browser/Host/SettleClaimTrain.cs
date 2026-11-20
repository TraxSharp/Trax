using System.Text.Json.Nodes;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

// What the browser tests' host runs. Two trains are scheduled at startup in two dependent manifest groups (the
// groups page's dependency graph) and a system-owned machine has an instance (the state-machine pages); the rest
// is what the seed builders (Fixtures/Seed.cs) drive into the states a console page shows: a train that fails
// while its key is down, one that holds while its subject is held, one whose queue entry stays staged, one with
// a Gate and a checkpoint, one that never runs, and two machines, one invoking a train and one with a timer.
namespace Trax.Samples.ApiDashboard.Browser.Host;

public class SettleClaimTrain : ServiceTrain<KeyedInput, Unit>, ISettleClaimTrain
{
    // Carried by the chain rather than registered, so no other train sees a decider.
    private static readonly IDecider Decider = new ScriptedDecider().YesNo<PayWithoutReview>(0.9);

    protected override Task<Either<Exception, Unit>> Junctions() =>
        AddServices(Decider)
            .Chain<LoadClaim>()
            .Gate<Claim, PayWithoutReview>(answer =>
                answer
                    .Yes(then => then.Chain<PayClaim>(), atLeast: 0.8)
                    .No(then => then.Chain<RejectClaim>(), below: 0.3)
                    .Unsure(then => then.Chain<RejectClaim>())
            )
            .Checkpoint<ClaimOutcome>()
            .Chain<NotifyClaimant>()
            .Resolve();
}

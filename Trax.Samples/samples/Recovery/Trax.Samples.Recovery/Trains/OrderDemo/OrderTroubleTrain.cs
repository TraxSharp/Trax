using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Faults;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

/// <summary>
/// The demo's control for the <c>order</c> machine, as "crash" is the refund run's: what goes wrong with the
/// caller's next order. The order's run id is built by the machine when the order is placed, so the trouble is armed
/// for the caller's next order rather than for a run, and that order's first step claims it.
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.User)]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Chooses what goes wrong with the next food order"
)]
public class OrderTroubleTrain
    : ServiceTrain<OrderTroubleInput, OrderTroubleOutput>,
        IOrderTroubleTrain
{
    protected override Task<Either<Exception, OrderTroubleOutput>> Junctions() =>
        Chain<ArmOrderTrouble>().Resolve();
}

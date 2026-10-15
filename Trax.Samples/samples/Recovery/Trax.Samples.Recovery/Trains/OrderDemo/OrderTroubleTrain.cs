using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

public interface IOrderTroubleTrain : IServiceTrain<OrderTroubleInput, OrderTroubleOutput>;

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

public class ArmOrderTrouble(FaultInjector faults, ISnapshotPrincipal caller)
    : Junction<OrderTroubleInput, OrderTroubleOutput>
{
    public override Task<OrderTroubleOutput> Run(OrderTroubleInput input)
    {
        // Keyed by the key the caller's orders are stored under, so only their own order claims it.
        var owner =
            caller.CurrentUserKey
            ?? throw new InvalidOperationException("Only a signed-in user has orders.");
        faults.ArmNextOrder(
            owner,
            input.Trouble switch
            {
                OrderTrouble.TabletTimesOut => CrashPoint.TabletTimesOut,
                OrderTrouble.RestaurantClosed => CrashPoint.RestaurantClosed,
                _ => CrashPoint.None,
            }
        );
        return Task.FromResult(new OrderTroubleOutput { Trouble = input.Trouble });
    }
}

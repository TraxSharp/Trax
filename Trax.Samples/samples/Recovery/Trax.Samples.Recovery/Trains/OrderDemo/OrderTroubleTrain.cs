using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

public interface IOrderTroubleTrain : IServiceTrain<OrderTroubleInput, OrderTroubleOutput>;

/// <summary>
/// The demo's control for the <c>order</c> machine, as "crash" is the refund run's: what goes wrong with the
/// next order placed. The order's run id is built by the machine when the order is placed, so the trouble is armed
/// for the next order rather than for a run, and that order's first step claims it.
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.Operator)]
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

public class ArmOrderTrouble(FaultInjector faults) : Junction<OrderTroubleInput, OrderTroubleOutput>
{
    public override Task<OrderTroubleOutput> Run(OrderTroubleInput input)
    {
        faults.ArmNextOrder(
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

using Shopfront.Fraud;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class RefuseOrderJunction(ILogger<RefuseOrderJunction> logger)
    : EffectJunction<OrderRisk, Unit>
{
    public override Task<Unit> Run(OrderRisk risk)
    {
        logger.LogWarning("Held a first order of {Total} from {CustomerId}", risk.Total, risk.CustomerId);
        throw new TrainException(
            "We could not take this order without a check. You have not been charged."
        );
    }
}

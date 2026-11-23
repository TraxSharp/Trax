using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Placed only by a customer: it holds the stock, checkpoints the hold, then fails, so there is a
/// run to resume.
/// </summary>
[TraxAuthorize(Roles = "Customer")]
public class CheckpointedOrderTrain
    : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>,
        ICheckpointedOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<HoldStockJunction>()
            .Checkpoint<StockHold>()
            .Chain<ChargeHeldOrderJunction>()
            .Resolve();
}

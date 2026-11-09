using Shopfront.Auth;
using Shopfront.Trains.ReturnOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.ReturnOrder;

// Run by a return's Checking state, for the customer who owns the return.
[TraxAuthorize(Roles = Roles.Customer)]
public class ReturnOrderTrain : ServiceTrain<ReturnOrderInput, ReturnOrderOutput>, IReturnOrderTrain
{
    protected override Task<Either<Exception, ReturnOrderOutput>> Junctions() =>
        Chain<CheckTheOrderJunction>().Resolve();
}

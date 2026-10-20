using Shopfront.Trains.Restock.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.Restock;

public class RestockTrain : ServiceTrain<RestockInput, RestockOutput>, IRestockTrain
{
    protected override Task<Either<Exception, RestockOutput>> Junctions() =>
        Chain<FindShortProductsJunction>().Chain<ReceiveDeliveryJunction>().Resolve();
}

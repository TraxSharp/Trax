using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.InventoryServer.Trains;

// Exposed at discover.inventory.getProduct. The "inventory" namespace is this server's
// schema grouping — it has nothing to do with the DI key a client picks to reach this
// server (see the Gateway sample).
[TraxAllowAnonymous]
[TraxQuery(Namespace = "inventory", Description = "Looks up a product by SKU.")]
public class GetProductTrain : ServiceTrain<GetProductInput, GetProductOutput>, IGetProductTrain
{
    protected override Task<Either<Exception, GetProductOutput>> Junctions() =>
        Chain<GetProductJunction>().Resolve();
}

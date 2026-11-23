using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

[TraxAllowAnonymous]
[TraxQuery(Description = "An order and its customer, whose orders lead back to the order.")]
public class FindOrder : ServiceTrain<FindOrderInput, OrderOutput>, IFindOrder
{
    protected override Task<Either<Exception, OrderOutput>> Junctions() =>
        Chain<FindOrderJunction>().Resolve();
}

using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

[TraxAllowAnonymous]
[TraxQuery(Description = "Liveness echo; keeps the root Query type non-empty.")]
public class Ping : ServiceTrain<PingInput, PingOutput>, IPing
{
    protected override Task<Either<Exception, PingOutput>> Junctions() =>
        Chain<PingJunction>().Resolve();
}

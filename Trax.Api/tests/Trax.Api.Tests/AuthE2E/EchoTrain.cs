using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

[TraxQuery(Namespace = "audit", Description = "Echoes input back.")]
[TraxAllowAnonymous]
public class EchoTrain : ServiceTrain<EchoInput, EchoOutput>, IEchoTrain
{
    protected override Task<Either<Exception, EchoOutput>> Junctions() =>
        Chain<EchoJunction>().Resolve();
}

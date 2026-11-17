using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

public class EffectTransformTrain : ServiceTrain<PersonDto, PersonEntity>, IEffectTransformTrain
{
    protected override Task<Either<Exception, PersonEntity>> Junctions() =>
        Chain<TransformJunction>().Resolve();
}

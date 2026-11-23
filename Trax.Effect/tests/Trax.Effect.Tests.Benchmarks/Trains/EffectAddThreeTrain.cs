using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

public class EffectAddThreeTrain : ServiceTrain<int, int>, IEffectAddThreeTrain
{
    protected override Task<Either<Exception, int>> Junctions() =>
        Chain<AddOneJunction>().Chain<AddOneJunction>().Chain<AddOneJunction>().Resolve();
}

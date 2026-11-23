using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Implementations ---

public class EffectAddOneTrain : ServiceTrain<int, int>, IEffectAddOneTrain
{
    protected override Task<Either<Exception, int>> Junctions() =>
        Chain<AddOneJunction>().Resolve();
}

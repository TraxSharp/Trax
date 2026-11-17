using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Scaling variants ---

public class EffectAddOneX1Train : ServiceTrain<int, int>, IEffectAddOneX1Train
{
    protected override Task<Either<Exception, int>> Junctions() =>
        Chain<AddOneJunction>().Resolve();
}

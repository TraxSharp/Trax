using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

public class EffectSimulatedIoTrain : ServiceTrain<int, int>, IEffectSimulatedIoTrain
{
    protected override Task<Either<Exception, int>> Junctions() =>
        Chain<SimulatedIoJunction>()
            .Chain<SimulatedIoJunction>()
            .Chain<SimulatedIoJunction>()
            .Resolve();
}

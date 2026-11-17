using Trax.Core.Functional;
using Trax.Core.Train;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Simulated I/O train ---

public class SimulatedIoTrain : Train<int, int>
{
    protected override Task<Either<Exception, int>> Junctions() =>
        Chain<SimulatedIoJunction>()
            .Chain<SimulatedIoJunction>()
            .Chain<SimulatedIoJunction>()
            .Resolve();
}

using Trax.Core.Functional;
using Trax.Core.Train;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Arithmetic trains (int -> int) ---

public class AddOneTrain : Train<int, int>
{
    protected override Task<Either<Exception, int>> Junctions() =>
        Chain<AddOneJunction>().Resolve();
}

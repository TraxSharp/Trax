using Trax.Core.Functional;
using Trax.Core.Train;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Transform train (PersonDto -> PersonEntity) ---

public class TransformTrain : Train<PersonDto, PersonEntity>
{
    protected override Task<Either<Exception, PersonEntity>> Junctions() =>
        Chain<TransformJunction>().Resolve();
}

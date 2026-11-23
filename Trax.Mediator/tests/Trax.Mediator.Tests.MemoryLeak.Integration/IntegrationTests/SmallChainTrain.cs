using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

// Test trains
public class SmallChainTrain : Train<SimpleInput, SimpleOutput>
{
    protected override async Task<Either<Exception, SimpleOutput>> Junctions() =>
        await Chain<ProcessJunction>().Resolve().ConfigureAwait(false);
}

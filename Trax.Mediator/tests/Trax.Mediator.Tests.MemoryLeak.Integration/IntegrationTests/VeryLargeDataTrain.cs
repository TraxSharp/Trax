using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public class VeryLargeDataTrain : Train<VeryLargeDataModel, SimpleOutput>
{
    protected override Task<Either<Exception, SimpleOutput>> Junctions() =>
        Chain<BuildLargeDataModel>().Chain<LargeDataJunction>().Resolve();
}

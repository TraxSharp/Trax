using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

public class FailingTestTrain : ServiceTrain<FailingTestInput, MemoryTestOutput>, IFailingTestTrain
{
    protected override Task<Either<Exception, MemoryTestOutput>> Junctions() =>
        Chain<FailAfterDelay>().Resolve();
}

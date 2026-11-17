using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

public class NestedTestTrain : ServiceTrain<NestedTestInput, NestedTestOutput>, INestedTestTrain
{
    protected override Task<Either<Exception, NestedTestOutput>> Junctions() =>
        Chain<AllocateNestedTestOutput>().Resolve();
}

using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Stress.Fakes.Trains;

public class StressTestTrain : ServiceTrain<StressTestInput, Unit>, IStressTestTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}

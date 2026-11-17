using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;

public class SchedulerTestTrain : ServiceTrain<SchedulerTestInput, Unit>, ISchedulerTestTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}

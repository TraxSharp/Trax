using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;

public class FailingSchedulerTestTrain
    : ServiceTrain<FailingSchedulerTestInput, Unit>,
        IFailingSchedulerTestTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<FailWithMessage>().Resolve();
}

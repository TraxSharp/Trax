using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A train that always fails, used for testing error handling.
/// </summary>
public class FailingSchedulerTestTrain
    : ServiceTrain<FailingSchedulerTestInput, Unit>,
        IFailingSchedulerTestTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<FailWithMessage>().Resolve();
}

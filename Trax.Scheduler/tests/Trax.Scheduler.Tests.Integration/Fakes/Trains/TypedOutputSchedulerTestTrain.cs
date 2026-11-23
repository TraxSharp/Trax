using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A test train with a typed (non-Unit) output, used to verify the scheduler
/// supports trains that return values other than Unit.
/// </summary>
public class TypedOutputSchedulerTestTrain
    : ServiceTrain<TypedOutputSchedulerTestInput, string>,
        ITypedOutputSchedulerTestTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ProcessTypedOutput>().Resolve();
}

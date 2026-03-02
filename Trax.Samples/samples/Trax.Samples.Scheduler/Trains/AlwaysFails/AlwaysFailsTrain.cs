using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Trains.AlwaysFails.Steps;

namespace Trax.Samples.Scheduler.Trains.AlwaysFails;

/// <summary>
/// A train that always fails by throwing an exception in its step.
/// Scheduled with MaxRetries(1) so it dead-letters almost immediately,
/// providing a convenient way to test the dead letter detail page.
/// </summary>
public class AlwaysFailsTrain : ServiceTrain<AlwaysFailsInput, Unit>, IAlwaysFailsTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(AlwaysFailsInput input) =>
        Activate(input).Chain<ThrowExceptionStep>().Resolve();
}

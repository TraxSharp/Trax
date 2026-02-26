using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Workflows.AlwaysFails.Steps;
using LanguageExt;

namespace Trax.Samples.Scheduler.Workflows.AlwaysFails;

/// <summary>
/// A workflow that always fails by throwing an exception in its step.
/// Scheduled with MaxRetries(1) so it dead-letters almost immediately,
/// providing a convenient way to test the dead letter detail page.
/// </summary>
public class AlwaysFailsWorkflow : ServiceTrain<AlwaysFailsInput, Unit>, IAlwaysFailsWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(AlwaysFailsInput input) =>
        Activate(input).Chain<ThrowExceptionStep>().Resolve();
}

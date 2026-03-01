using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Workflows.JobDispatcher.Steps;

namespace Trax.Scheduler.Workflows.JobDispatcher;

/// <summary>
/// Picks queued work queue entries and dispatches them as background tasks.
/// </summary>
public class JobDispatcherWorkflow : ServiceTrain<Unit, Unit>, IJobDispatcherWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
        Activate(input)
            .Chain<LoadQueuedJobsStep>()
            .Chain<LoadDispatchCapacityStep>()
            .Chain<ApplyCapacityLimitsStep>()
            .Chain<DispatchJobsStep>()
            .Resolve();
}

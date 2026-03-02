using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.JobDispatcher.Steps;

namespace Trax.Scheduler.Trains.JobDispatcher;

/// <summary>
/// Picks queued work queue entries and dispatches them as background tasks.
/// </summary>
public class JobDispatcherTrain : ServiceTrain<Unit, Unit>, IJobDispatcherTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
        Activate(input)
            .Chain<LoadQueuedJobsStep>()
            .Chain<LoadDispatchCapacityStep>()
            .Chain<ApplyCapacityLimitsStep>()
            .Chain<DispatchJobsStep>()
            .Resolve();
}

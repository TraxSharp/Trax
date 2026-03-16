using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.JobDispatcher.Junctions;

namespace Trax.Scheduler.Trains.JobDispatcher;

/// <summary>
/// Picks queued work queue entries and dispatches them as background tasks.
/// </summary>
public class JobDispatcherTrain : ServiceTrain<Unit, Unit>, IJobDispatcherTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
        Activate(input)
            .Chain<LoadQueuedJobsJunction>()
            .Chain<LoadDispatchCapacityJunction>()
            .Chain<ApplyCapacityLimitsJunction>()
            .Chain<DispatchJobsJunction>()
            .Resolve();
}

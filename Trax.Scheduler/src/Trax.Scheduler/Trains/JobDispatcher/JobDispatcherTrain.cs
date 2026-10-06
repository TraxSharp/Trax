using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.JobDispatcher.Junctions;

namespace Trax.Scheduler.Trains.JobDispatcher;

/// <summary>
/// Picks queued work queue entries and dispatches them as background tasks.
/// Infrastructure the scheduler registers and runs itself; not intended to be called directly.
/// </summary>
internal class JobDispatcherTrain : ServiceTrain<Unit, Unit>, IJobDispatcherTrain
{
    /// <summary>
    /// Loads queued work queue entries, reads the free dispatch capacity, trims the batch to the
    /// global and per-group limits, then dispatches what remains, in that order.
    /// </summary>
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LoadQueuedJobsJunction>()
            .Chain<LoadDispatchCapacityJunction>()
            .Chain<ApplyCapacityLimitsJunction>()
            .Chain<DispatchJobsJunction>()
            .Resolve();
}

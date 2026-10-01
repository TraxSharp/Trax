using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.JobRunner.Junctions;

namespace Trax.Scheduler.Trains.JobRunner;

/// <summary>
/// Runs scheduled train jobs that have been dispatched by the JobDispatcher.
/// </summary>
/// <remarks>
/// This train:
/// 1. Loads the metadata and manifest from the database
/// 2. Executes the scheduled train via TrainBus, then records the success on its manifest
///    (LastSuccessfulRun, NextScheduledRun, Once auto-disable) on an uncancellable token
///
/// A job can be delivered more than once (an at-least-once queue, a retried dispatch, a job
/// claimed twice). Only the delivery that moves the run's row out of <c>Pending</c> runs the
/// train; any other completes without running it and records nothing, so its transport
/// acknowledges the delivery instead of retrying it or recording a failure.
/// </remarks>
public class JobRunnerTrain : ServiceTrain<RunJobRequest, Unit>, IJobRunnerTrain
{
    /// <summary>
    /// Loads the job's metadata and manifest, then runs the scheduled train unless another
    /// delivery already started it and, when it succeeds, records the run on its manifest.
    /// </summary>
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LoadMetadataJunction>().Chain<RunScheduledTrainJunction>().Resolve();
}

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
/// 2. Validates the train state is Pending
/// 3. Executes the scheduled train via TrainBus, then records the success on its manifest
///    (LastSuccessfulRun, NextScheduledRun, Once auto-disable) on an uncancellable token
/// </remarks>
public class JobRunnerTrain : ServiceTrain<RunJobRequest, Unit>, IJobRunnerTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LoadMetadataJunction>()
            .Chain<ValidateMetadataStateJunction>()
            .Chain<RunScheduledTrainJunction>()
            .Resolve();
}

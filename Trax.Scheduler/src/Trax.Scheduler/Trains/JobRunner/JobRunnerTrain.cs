using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.JobRunner.Steps;

namespace Trax.Scheduler.Trains.JobRunner;

/// <summary>
/// Runs scheduled train jobs that have been dispatched by the JobDispatcher.
/// </summary>
/// <remarks>
/// This train:
/// 1. Loads the metadata and manifest from the database
/// 2. Validates the train state is Pending
/// 3. Executes the scheduled train via TrainBus
/// 4. Updates the manifest's LastSuccessfulRun timestamp
/// </remarks>
public class JobRunnerTrain : ServiceTrain<RunJobRequest, Unit>, IJobRunnerTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(RunJobRequest input) =>
        Activate(input)
            .Chain<LoadMetadataStep>()
            .Chain<ValidateMetadataStateStep>()
            .Chain<RunScheduledTrainStep>()
            .Chain<UpdateManifestSuccessStep>()
            .Chain<SaveDatabaseChangesStep>()
            .Resolve();
}

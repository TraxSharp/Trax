using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.TaskServerExecutor.Steps;

namespace Trax.Scheduler.Trains.TaskServerExecutor;

/// <summary>
/// Executes train jobs that have been scheduled via the manifest system.
/// </summary>
/// <remarks>
/// This train:
/// 1. Loads the metadata and manifest from the database
/// 2. Validates the train state is Pending
/// 3. Executes the scheduled train via TrainBus
/// 4. Updates the manifest's LastSuccessfulRun timestamp
/// </remarks>
public class TaskServerExecutorTrain
    : ServiceTrain<ExecuteManifestRequest, Unit>,
        ITaskServerExecutorTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        ExecuteManifestRequest input
    ) =>
        Activate(input)
            .Chain<LoadMetadataStep>()
            .Chain<ValidateMetadataStateStep>()
            .Chain<ExecuteScheduledTrainStep>()
            .Chain<UpdateManifestSuccessStep>()
            .Chain<SaveDatabaseChangesStep>()
            .Resolve();
}

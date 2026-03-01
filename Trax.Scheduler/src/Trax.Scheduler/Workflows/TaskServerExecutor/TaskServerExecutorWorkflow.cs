using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Workflows.TaskServerExecutor.Steps;

namespace Trax.Scheduler.Workflows.TaskServerExecutor;

/// <summary>
/// Executes workflow jobs that have been scheduled via the manifest system.
/// </summary>
/// <remarks>
/// This workflow:
/// 1. Loads the metadata and manifest from the database
/// 2. Validates the workflow state is Pending
/// 3. Executes the scheduled workflow via WorkflowBus
/// 4. Updates the manifest's LastSuccessfulRun timestamp
/// </remarks>
public class TaskServerExecutorWorkflow
    : ServiceTrain<ExecuteManifestRequest, Unit>,
        ITaskServerExecutorWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        ExecuteManifestRequest input
    ) =>
        Activate(input)
            .Chain<LoadMetadataStep>()
            .Chain<ValidateMetadataStateStep>()
            .Chain<ExecuteScheduledWorkflowStep>()
            .Chain<UpdateManifestSuccessStep>()
            .Chain<SaveDatabaseChangesStep>()
            .Resolve();
}

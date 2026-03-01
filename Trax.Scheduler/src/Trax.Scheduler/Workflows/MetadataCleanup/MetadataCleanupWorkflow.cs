using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Workflows.MetadataCleanup.Steps;

namespace Trax.Scheduler.Workflows.MetadataCleanup;

/// <summary>
/// Deletes expired metadata entries for configured workflow types.
/// </summary>
public class MetadataCleanupWorkflow
    : ServiceTrain<MetadataCleanupRequest, Unit>,
        IMetadataCleanupWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        MetadataCleanupRequest input
    ) => Activate(input).Chain<DeleteExpiredMetadataStep>().Resolve();
}

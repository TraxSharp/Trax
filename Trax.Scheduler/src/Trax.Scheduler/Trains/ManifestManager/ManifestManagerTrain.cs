using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.ManifestManager.Junctions;

namespace Trax.Scheduler.Trains.ManifestManager;

/// <summary>
/// Orchestrates the manifest-based job scheduling system.
/// </summary>
public class ManifestManagerTrain : ServiceTrain<Unit, Unit>, IManifestManagerTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LoadManifestsJunction>()
            .Chain<CancelTimedOutJobsJunction>()
            .Chain<ReapStalePendingMetadataJunction>()
            .Chain<ReapStaleInProgressMetadataJunction>()
            .Chain<ResolveStaleStagedEntriesJunction>()
            .Chain<ReapFailedJobsJunction>()
            .Chain<DetermineJobsToQueueJunction>()
            .Chain<CreateWorkQueueEntriesJunction>()
            .Resolve();
}

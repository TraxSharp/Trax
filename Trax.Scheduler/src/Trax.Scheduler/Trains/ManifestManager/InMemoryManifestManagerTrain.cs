using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.ManifestManager.Junctions;

namespace Trax.Scheduler.Trains.ManifestManager;

/// <summary>
/// InMemory-compatible manifest manager that skips PostgreSQL-specific junctions
/// and dispatches jobs directly via <see cref="Junctions.InMemoryDispatchJobsJunction"/>.
/// Infrastructure the scheduler registers and runs itself; not intended to be called directly.
/// </summary>
/// <remarks>
/// The standard <see cref="ManifestManagerTrain"/> includes junctions that use
/// <c>ExecuteUpdateAsync</c> (CancelTimedOutJobsJunction, ReapStalePendingMetadataJunction) and
/// creates WorkQueue entries consumed by the JobDispatcher via <c>FOR UPDATE SKIP LOCKED</c>.
/// None of these operations are supported by the EF Core InMemory provider.
///
/// This train omits those junctions and replaces <see cref="CreateWorkQueueEntriesJunction"/> with
/// <see cref="InMemoryDispatchJobsJunction"/>, which creates Metadata and dispatches inline
/// via <see cref="Services.JobSubmitter.InMemoryJobSubmitter"/>.
/// </remarks>
internal class InMemoryManifestManagerTrain : ServiceTrain<Unit, Unit>, IManifestManagerTrain
{
    /// <summary>
    /// Loads manifests, reaps failed jobs, determines which manifests are due, and dispatches them
    /// inline. The timeout, stale-metadata and work-queue junctions of
    /// <see cref="ManifestManagerTrain"/> are left out (see the remarks on the type).
    /// </summary>
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LoadManifestsJunction>()
            .Chain<ReapFailedJobsJunction>()
            .Chain<DetermineJobsToQueueJunction>()
            .Chain<InMemoryDispatchJobsJunction>()
            .Resolve();
}

using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.ManifestManager.Junctions;

namespace Trax.Scheduler.Trains.ManifestManager;

/// <summary>
/// InMemory-compatible manifest manager that skips PostgreSQL-specific junctions
/// and dispatches jobs directly via <see cref="Junctions.InMemoryDispatchJobsJunction"/>.
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
public class InMemoryManifestManagerTrain : ServiceTrain<Unit, Unit>, IManifestManagerTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
        Activate(input)
            .Chain<LoadManifestsJunction>()
            .Chain<ReapFailedJobsJunction>()
            .Chain<DetermineJobsToQueueJunction>()
            .Chain<InMemoryDispatchJobsJunction>()
            .Resolve();
}

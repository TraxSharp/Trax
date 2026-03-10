using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.ManifestManager.Steps;

namespace Trax.Scheduler.Trains.ManifestManager;

/// <summary>
/// InMemory-compatible manifest manager that skips PostgreSQL-specific steps
/// and dispatches jobs directly via <see cref="Steps.InMemoryDispatchJobsStep"/>.
/// </summary>
/// <remarks>
/// The standard <see cref="ManifestManagerTrain"/> includes steps that use
/// <c>ExecuteUpdateAsync</c> (CancelTimedOutJobsStep, ReapStalePendingMetadataStep) and
/// creates WorkQueue entries consumed by the JobDispatcher via <c>FOR UPDATE SKIP LOCKED</c>.
/// None of these operations are supported by the EF Core InMemory provider.
///
/// This train omits those steps and replaces <see cref="CreateWorkQueueEntriesStep"/> with
/// <see cref="InMemoryDispatchJobsStep"/>, which creates Metadata and dispatches inline
/// via <see cref="Services.JobSubmitter.InMemoryJobSubmitter"/>.
/// </remarks>
public class InMemoryManifestManagerTrain : ServiceTrain<Unit, Unit>, IManifestManagerTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
        Activate(input)
            .Chain<LoadManifestsStep>()
            .Chain<ReapFailedJobsStep>()
            .Chain<DetermineJobsToQueueStep>()
            .Chain<InMemoryDispatchJobsStep>()
            .Resolve();
}

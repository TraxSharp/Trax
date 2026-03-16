using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.MetadataCleanup.Junctions;

namespace Trax.Scheduler.Trains.MetadataCleanup;

/// <summary>
/// Deletes expired metadata entries for configured train types.
/// </summary>
public class MetadataCleanupTrain
    : ServiceTrain<MetadataCleanupRequest, Unit>,
        IMetadataCleanupTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        MetadataCleanupRequest input
    ) => Activate(input).Chain<DeleteExpiredMetadataJunction>().Resolve();
}

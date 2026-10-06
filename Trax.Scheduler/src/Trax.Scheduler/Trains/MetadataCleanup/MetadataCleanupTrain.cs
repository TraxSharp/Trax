using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Trains.MetadataCleanup.Junctions;

namespace Trax.Scheduler.Trains.MetadataCleanup;

/// <summary>
/// Deletes expired metadata entries for configured train types.
/// Infrastructure the scheduler registers and runs itself; not intended to be called directly.
/// </summary>
internal class MetadataCleanupTrain
    : ServiceTrain<MetadataCleanupRequest, Unit>,
        IMetadataCleanupTrain
{
    /// <summary>
    /// A single junction that deletes, in batches, terminal (Completed or Failed) metadata past its
    /// retention period, with its work queue and log rows, for the whitelisted train types and the
    /// scheduler's own trains.
    /// </summary>
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<DeleteExpiredMetadataJunction>().Resolve();
}

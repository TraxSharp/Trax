using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduling.Trains.ImportSupplierFeed.Junctions;

namespace Trax.Samples.Scheduling.Trains.ImportSupplierFeed;

/// <summary>
/// Fails while the supplier is down. Each failure is retried after a backoff; once the failures
/// pass the manifest's <c>MaxRetries</c>, the manifest is dead-lettered and waits for an operator
/// to requeue or acknowledge it.
/// </summary>
public class ImportSupplierFeedTrain
    : ServiceTrain<ImportSupplierFeedInput, ImportSupplierFeedOutput>,
        IImportSupplierFeedTrain
{
    protected override Task<Either<Exception, ImportSupplierFeedOutput>> Junctions() =>
        Chain<DownloadFeedJunction>().Resolve();
}

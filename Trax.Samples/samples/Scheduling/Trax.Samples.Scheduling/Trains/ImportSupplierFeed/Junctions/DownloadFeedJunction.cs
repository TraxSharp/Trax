using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.Scheduling.Services;

namespace Trax.Samples.Scheduling.Trains.ImportSupplierFeed.Junctions;

public class DownloadFeedJunction(SupplierFeed feed, ILogger<DownloadFeedJunction> logger)
    : Junction<ImportSupplierFeedInput, ImportSupplierFeedOutput>
{
    public override Task<ImportSupplierFeedOutput> Run(ImportSupplierFeedInput input)
    {
        var products = feed.FetchProducts(input.Supplier);
        logger.LogInformation(
            "Imported {Count} products from {Supplier}",
            products.Count,
            input.Supplier
        );
        return Task.FromResult(new ImportSupplierFeedOutput(input.Supplier, products.Count));
    }
}

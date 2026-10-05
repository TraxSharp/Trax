using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduling.Trains.ImportSupplierFeed;

public record ImportSupplierFeedInput : IManifestProperties
{
    public string Supplier { get; init; } = "acme";
}

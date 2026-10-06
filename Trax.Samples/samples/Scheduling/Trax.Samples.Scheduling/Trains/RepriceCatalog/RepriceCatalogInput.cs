using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduling.Trains.RepriceCatalog;

public record RepriceCatalogInput : IManifestProperties
{
    public string Catalog { get; init; } = "storefront";
}

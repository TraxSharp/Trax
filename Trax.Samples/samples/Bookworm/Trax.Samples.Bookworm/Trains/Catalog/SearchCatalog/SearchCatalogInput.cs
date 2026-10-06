using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Bookworm.Trains.Catalog.SearchCatalog;

public record SearchCatalogInput : IManifestProperties
{
    /// <summary>Case-insensitive substring matched against book titles.</summary>
    public required string Query { get; init; }
}

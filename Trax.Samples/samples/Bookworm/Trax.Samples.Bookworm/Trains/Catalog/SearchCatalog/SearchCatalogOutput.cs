namespace Trax.Samples.Bookworm.Trains.Catalog.SearchCatalog;

public record SearchCatalogOutput
{
    public required IReadOnlyList<BookSummary> Books { get; init; }
}

namespace Trax.Samples.Bookworm.Trains.Catalog.SearchCatalog;

/// <summary>A flat projection of a catalog book returned by the search train.</summary>
public record BookSummary
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required string Isbn { get; init; }
}

using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook;

/// <summary>The book to borrow. The borrower is the caller's own member record, never an input.</summary>
public record BorrowBookInput : IManifestProperties
{
    /// <summary>The catalog book to borrow. Checked against the catalog, cross-schema.</summary>
    public required int BookId { get; init; }
}

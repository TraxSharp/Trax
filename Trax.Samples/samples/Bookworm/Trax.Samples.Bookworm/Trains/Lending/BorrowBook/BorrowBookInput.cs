using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook;

public record BorrowBookInput : IManifestProperties
{
    public required int MemberId { get; init; }

    /// <summary>The catalog book to borrow. Resolved cross-schema; not validated against the catalog here.</summary>
    public required int BookId { get; init; }
}

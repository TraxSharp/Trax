using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Bookworm.Trains.Lending.ReturnBook;

public record ReturnBookInput : IManifestProperties
{
    public required int LoanId { get; init; }
}

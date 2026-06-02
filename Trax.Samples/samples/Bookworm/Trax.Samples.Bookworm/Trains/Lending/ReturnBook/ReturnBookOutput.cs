namespace Trax.Samples.Bookworm.Trains.Lending.ReturnBook;

public record ReturnBookOutput
{
    public required int LoanId { get; init; }
    public required DateTime ReturnedAt { get; init; }
}

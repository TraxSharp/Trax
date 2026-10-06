namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook;

public record BorrowBookOutput
{
    public required int LoanId { get; init; }
    public required DateTime BorrowedAt { get; init; }
    public required DateTime DueAt { get; init; }
}

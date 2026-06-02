using Trax.Core.Junction;
using Trax.Samples.Bookworm.Lending.Context;
using Trax.Samples.Bookworm.Lending.Models.Loans;
using Trax.Samples.Bookworm.Services;

namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook.Junctions;

/// <summary>Inserts a loan row and computes its due date from the loan policy.</summary>
public class BorrowBookJunction(ILendingDbContext lending, ILoanPolicy loanPolicy)
    : Junction<BorrowBookInput, BorrowBookOutput>
{
    public override async Task<BorrowBookOutput> Run(BorrowBookInput input)
    {
        var borrowedAt = DateTime.UtcNow;

        var loan = new Loan
        {
            MemberId = input.MemberId,
            BookId = input.BookId,
            BorrowedAt = borrowedAt,
        };

        lending.Loans.Add(loan);
        await lending.SaveChangesAsync();

        return new BorrowBookOutput
        {
            LoanId = loan.Id,
            BorrowedAt = borrowedAt,
            DueAt = loanPolicy.DueDate(borrowedAt),
        };
    }
}

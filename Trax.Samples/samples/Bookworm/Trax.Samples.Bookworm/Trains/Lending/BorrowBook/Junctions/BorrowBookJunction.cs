using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Samples.Bookworm.Catalog.Context;
using Trax.Samples.Bookworm.Lending.Context;
using Trax.Samples.Bookworm.Lending.Models.Loans;
using Trax.Samples.Bookworm.Services;

namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook.Junctions;

/// <summary>
/// Lends a book to the caller. The borrower is the member row the lending filter lets the caller
/// read as their own; the book must exist in the catalog (a cross-schema read through
/// <see cref="ICatalogDbContext"/>) and must not be on loan already.
/// </summary>
public class BorrowBookJunction(
    ILendingDbContext lending,
    ICatalogDbContext catalog,
    ILendingCaller caller,
    ILoanPolicy loanPolicy
) : Junction<BorrowBookInput, BorrowBookOutput>
{
    public override async Task<BorrowBookOutput> Run(BorrowBookInput input)
    {
        var member =
            await lending.Members.FirstOrDefaultAsync(m => m.PrincipalId == caller.PrincipalId)
            ?? throw new TrainException("Your credential is not linked to a library member.");

        if (!await catalog.Books.AnyAsync(b => b.Id == input.BookId))
            throw new TrainException($"Book {input.BookId} is not in the catalog.");

        // Availability depends on every member's loans, not only the caller's, so this one read
        // steps outside the owner filter. It returns a yes or no and no row.
        var onLoan = await lending
            .Loans.IgnoreQueryFilters()
            .AnyAsync(l => l.BookId == input.BookId && l.ReturnedAt == null);
        if (onLoan)
            throw new TrainException($"Book {input.BookId} is already on loan.");

        var borrowedAt = DateTime.UtcNow;
        var loan = new Loan
        {
            MemberId = member.Id,
            BookId = input.BookId,
            BorrowedAt = borrowedAt,
        };
        lending.Loans.Add(loan);

        try
        {
            await lending.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            // Another borrow of the same book committed between the check and this insert; the
            // partial unique index on open loans refused the second one.
            throw new TrainException($"Book {input.BookId} is already on loan.");
        }

        return new BorrowBookOutput
        {
            LoanId = loan.Id,
            BorrowedAt = borrowedAt,
            DueAt = loanPolicy.DueDate(borrowedAt),
        };
    }
}

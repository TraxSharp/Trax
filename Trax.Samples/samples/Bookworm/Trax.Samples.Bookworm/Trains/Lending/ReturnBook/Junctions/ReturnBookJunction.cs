using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.Bookworm.Lending.Context;

namespace Trax.Samples.Bookworm.Trains.Lending.ReturnBook.Junctions;

/// <summary>Sets the loan's returned timestamp.</summary>
public class ReturnBookJunction(ILendingDbContext lending)
    : Junction<ReturnBookInput, ReturnBookOutput>
{
    public override async Task<ReturnBookOutput> Run(ReturnBookInput input)
    {
        var loan =
            await lending.Loans.FirstOrDefaultAsync(l => l.Id == input.LoanId)
            ?? throw new InvalidOperationException($"Loan {input.LoanId} not found.");

        if (loan.ReturnedAt is not null)
            throw new InvalidOperationException($"Loan {input.LoanId} was already returned.");

        var returnedAt = DateTime.UtcNow;
        loan.ReturnedAt = returnedAt;
        await lending.SaveChangesAsync();

        return new ReturnBookOutput { LoanId = loan.Id, ReturnedAt = returnedAt };
    }
}

using Microsoft.EntityFrameworkCore;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Samples.Bookworm.Lending.Context;

namespace Trax.Samples.Bookworm.Trains.Lending.ReturnBook.Junctions;

/// <summary>
/// Sets the loan's returned timestamp. The loan is looked up through the lending filter, so a member
/// can return only their own loans (another member's loan is "not found"), and a librarian any loan.
/// </summary>
public class ReturnBookJunction(ILendingDbContext lending)
    : Junction<ReturnBookInput, ReturnBookOutput>
{
    public override async Task<ReturnBookOutput> Run(ReturnBookInput input)
    {
        var loan =
            await lending.Loans.FirstOrDefaultAsync(l => l.Id == input.LoanId)
            ?? throw new TrainException($"Loan {input.LoanId} not found.");

        if (loan.ReturnedAt is not null)
            throw new TrainException($"Loan {input.LoanId} was already returned.");

        var returnedAt = DateTime.UtcNow;
        loan.ReturnedAt = returnedAt;
        await lending.SaveChangesAsync();

        return new ReturnBookOutput { LoanId = loan.Id, ReturnedAt = returnedAt };
    }
}

using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Lending.Models.Loans;
using Trax.Samples.Bookworm.Lending.Models.Members;

namespace Trax.Samples.Bookworm.Lending.Context;

/// <summary>Companion interface for <see cref="LendingDbContext"/>.</summary>
public interface ILendingDbContext : IDomainDataContext
{
    DbSet<Member> Members { get; }
    DbSet<Loan> Loans { get; }
}

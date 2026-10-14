// What the compiled examples on effect/scope-a-data-context-to-the-caller.md take as given.
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

public class Member
{
    public int Id { get; set; }
    public string? PrincipalId { get; set; }
}

public class Loan
{
    public int Id { get; set; }
    public int MemberId { get; set; }
}

public interface ILendingDbContext : IDomainDataContext
{
    DbSet<Member> Members { get; }
    DbSet<Loan> Loans { get; }
}

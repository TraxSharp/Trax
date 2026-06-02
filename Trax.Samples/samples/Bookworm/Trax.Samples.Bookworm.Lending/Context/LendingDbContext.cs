using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Lending.Models.Loans;
using Trax.Samples.Bookworm.Lending.Models.Members;

namespace Trax.Samples.Bookworm.Lending.Context;

/// <summary>
/// The lending domain's data context. Owns the <c>lending</c> schema. Holds no reference to the
/// catalog domain: a loan's <see cref="Loan.BookId"/> is a plain integer, and the relationship to a
/// catalog book is resolved at the GraphQL layer by the cross-schema edge project.
/// </summary>
public class LendingDbContext(DbContextOptions<LendingDbContext> options)
    : DomainDataContext<LendingDbContext>(options),
        ILendingDbContext
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override string Schema => LendingSchema.Name;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Member>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Loan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            // Same-schema FK to Member, configured without a navigation property so the loan stays a
            // flat scalar shape in GraphQL.
            entity
                .HasOne<Member>()
                .WithMany()
                .HasForeignKey(e => e.MemberId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.BookId);
        });
    }
}
